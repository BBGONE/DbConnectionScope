using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Transactions;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Bell.PPS.Database.Shared
{
    /// <summary>
    /// Options for modifying how DbConnectionScope.Current is affected while constructing a new scope.
    /// </summary>
    public enum DbConnectionScopeOption
    {
        Required,                   // Set self as currentScope if there isn't one already on the thread, otherwise don't do anything.
        RequiresNew                 // Push self as currentScope (track prior scope and restore it on dispose).
    }

    // Allows almost-automated re-use of connections across multiple call levels
    //  while still controlling connection lifetimes.  Multiple connections are supported within a single scope.
    /// <summary>
    /// Class to assist in managing connection lifetimes inside scopes.
    /// </summary>
    public sealed class DbConnectionScope : IDisposable
    {
        private static readonly string SLOT_KEY = Guid.NewGuid().ToString();
#if TEST
        //Just for Testing Purposes
        public static int GetScopeStoreCount() {
            return __scopeStore.Count;
        }
#endif

#region class fields
        private static ConcurrentDictionary<Guid, WeakReference<DbConnectionScope>> __scopeStore = new ConcurrentDictionary<Guid, WeakReference<DbConnectionScope>>();
        private static DbConnectionScope __currentScope
        {
            get
            {
                object res = CallContext.LogicalGetData(SLOT_KEY);
                if (res != null)
                {
                    Guid scopeID = (Guid)res;
                    WeakReference<DbConnectionScope> wref;
                    DbConnectionScope scope;
                    if (__scopeStore.TryGetValue(scopeID, out wref))
                    {
                        if (wref.TryGetTarget(out scope))
                            return scope;
                        else
                            return null;
                    }
                    else
                    {
                        return null;
                    }
                }
                return null;
            }
            set
            {
                Guid? id = value == null ? (Guid?)null : value.UNIQUE_ID;
                if (id.HasValue)
                {
                    CallContext.LogicalSetData(SLOT_KEY, id);
                }
                else
                    CallContext.FreeNamedDataSlot(SLOT_KEY);
            }
        }

        private static ConditionalWeakTable<DbConnection, Task<DbConnection>> __openAsyncTasks =  new ConditionalWeakTable<DbConnection, Task<DbConnection>>();
#endregion

#region instance fields
        internal readonly Guid UNIQUE_ID = Guid.NewGuid();
        private readonly object SyncRoot = new object();
        private DbConnectionScope _outerScope;
        private string _transId;
        private DbConnectionScopeOption _option;
        private Lazy<ConcurrentDictionary<string, DbConnection>> _connections;
        private bool _isDisposed;
#endregion

#region class methods and properties
        /// <summary>
        /// Obtain the currently active connection scope
        /// </summary>
        public static DbConnectionScope Current
        {
            get
            {
                return __currentScope;
            }
        }

        public static TConnection GetOpenConnection<TConnection>(IDbConnectionFactory factory, string connectionName)
            where TConnection: DbConnection
        {
            return (TConnection)DbConnectionScope.Current.GetOpenConnection(factory, connectionName);
        }

        public static async Task<TConnection> GetOpenConnectionAsync<TConnection>(IDbConnectionFactory factory, string connectionName)
            where TConnection : DbConnection
        {
            return (TConnection) await DbConnectionScope.Current.GetOpenConnectionAsync(factory, connectionName);
        }

        private static string CurrentTransactionId
        {
            get
            {
                string currTransId = string.Empty;
                var currTran = Transaction.Current;
                if (currTran != null)
                {
                    currTransId = currTran.TransactionInformation.LocalIdentifier;
                }
                return currTransId;
            }
        }
#endregion

#region constructor annd destructor
        /// <summary>
        /// Default Constructor
        /// </summary>
        public DbConnectionScope()
            : this(DbConnectionScopeOption.Required)
        {
        }

        /// <summary>
        /// Constructor with options
        /// </summary>
        /// <param name="option">Option for how to modify Current during constructor</param>
        public DbConnectionScope(DbConnectionScopeOption option)
        {
            _isDisposed = true;  // short circuit Dispose until we're properly set up
            this._transId = CurrentTransactionId;
            this._option = option;
            this._outerScope = null;

            DbConnectionScope outerScope = __currentScope;
            bool isAllocateOk = (outerScope == null || outerScope._transId != this._transId);
            if (option == DbConnectionScopeOption.RequiresNew ||
               (option == DbConnectionScopeOption.Required && isAllocateOk))
            {
                // only bother allocating dictionary if we're going to push
                _connections = new Lazy<ConcurrentDictionary<string,DbConnection>>(()=> new ConcurrentDictionary<string,DbConnection>(), true);

                // Devnote:  Order of initial assignment is important in cases of failure!
                if (__scopeStore.TryAdd(this.UNIQUE_ID, new WeakReference<DbConnectionScope>(this)))
                {
                    _outerScope = outerScope;
                    _isDisposed = false;
                    __currentScope = this;
                }
            }
        }

        ~DbConnectionScope()
        {
            Dispose(false);
        }
#endregion

#region public instance methods and properties
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public bool TryGetConnection(string connectionName, out DbConnection connection)
        {
            this.CheckTransaction();
            lock (this.SyncRoot)
            {
                return TryGetConnectionByName(connectionName, out connection);
            }
        }

        public DbConnection GetConnection(IDbConnectionFactory factory, string connectionName)
        {
            this.CheckTransaction();
            DbConnection result = null;

            lock (this.SyncRoot)
            {
                if (!this.TryGetConnectionByName(this, connectionName, out result))
                {
                    result = factory.CreateConnection(connectionName);
                    _connections.Value.TryAdd(connectionName, result);
                }
            }
            return result;
        }

        public DbConnection GetOpenConnection(IDbConnectionFactory factory, string connectionName)
        {
            return this.GetOpenConnectionInternal(factory, connectionName, 0);
        }

        public Task<DbConnection> GetOpenConnectionAsync(IDbConnectionFactory factory, string connectionName)
        {
            return this.GetOpenConnectionAsyncInternal(factory, connectionName, 0);
        }

        public DbConnectionScopeOption Option
        {
            get { return _option; }
        }
#endregion

#region private methods and properties
        private DbConnection GetOpenConnectionInternal(IDbConnectionFactory factory, string connectionName, int level)
        {
            if (level > 1)
                throw new OverflowException(string.Format("Exceeded maximum times to get open connection: {0}", connectionName));
            DbConnection result = this.GetConnection(factory, connectionName);
            try
            {
                lock (result)
                {
                    Task<DbConnection> openAsyncTask;
                    if (__openAsyncTasks.TryGetValue(result, out openAsyncTask))
                    {
                        openAsyncTask.Wait();
                    }
                    else
                    {
                        if (result.State == ConnectionState.Closed)
                            result.Open();
                        else if (result.State == ConnectionState.Broken && TryRemoveConnection(result))
                        {
                            return GetOpenConnectionInternal(factory, connectionName, level+1);
                        }
                    }
                }
                return result;
            }
            catch
            {
                TryRemoveConnection(result);
                throw;
            }
        }

        private Task<DbConnection> GetOpenConnectionAsyncInternal(IDbConnectionFactory factory, string connectionName, int level)
        {
            if (level > 1)
            {
                var error = new OverflowException(string.Format("Exceeded maximum times to get open connection: {0}", connectionName));
                TaskCompletionSource<DbConnection> tcs = new TaskCompletionSource<DbConnection>();
                tcs.SetException(error);
                return tcs.Task;
            }
         
            DbConnection result = this.GetConnection(factory, connectionName);
            lock (result)
            {
                Task<DbConnection> openAsyncTask;
                if (__openAsyncTasks.TryGetValue(result, out openAsyncTask))
                {
                    return openAsyncTask;
                }

                if (result.State == ConnectionState.Closed)
                {
                    TaskCompletionSource<DbConnection> tcs = new TaskCompletionSource<DbConnection>();
                    var task = result.OpenAsync();
                    task.ContinueWith((antecedent) =>
                    {
                        if (_isDisposed)
                        {
                            tcs.SetCanceled();
                            return;
                        }

                        try
                        {
                            if (antecedent.IsFaulted)
                            {
                                TryRemoveConnection(result);
                                tcs.SetException(antecedent.Exception);
                            }
                            else if (antecedent.IsCanceled)
                            {
                                tcs.SetCanceled();
                            }
                            else
                            {
                                tcs.SetResult(result);
                            }
                        }
                        finally
                        {
                            __openAsyncTasks.Remove(result);
                        }
                    });
                    openAsyncTask = tcs.Task;
                    __openAsyncTasks.Add(result, openAsyncTask);
                    return openAsyncTask;
                }
                else if (result.State == ConnectionState.Broken && TryRemoveConnection(result))
                {
                    return GetOpenConnectionAsyncInternal(factory, connectionName, level+1);
                }
                else
                {
                    return Task.FromResult(result);
                }
            }
        }

        /// <summary>
        /// In case of DbConnectionScopeOption equals Required  
        /// it returns outer scope with the same transaction id on the scope
        /// typically it will be when TransactionScopeOption is Suppress on this scope and the outer scope
        /// </summary>
        /// <param name="resultScope"></param>
        /// <returns></returns>
        private bool TryGetCompatableScope(out DbConnectionScope resultScope)
        {
            resultScope = null;
            if (this._option == DbConnectionScopeOption.RequiresNew)
                return false;
            resultScope = this._outerScope;
            while (resultScope != null)
            {
                //find the outer scope with the same transaction id
                if (!resultScope._isDisposed && resultScope._transId == this._transId)
                    break;
                else
                    resultScope = resultScope._outerScope;
            }
            return resultScope != null;
        }

        private bool TryGetConnectionByName(DbConnectionScope scope, string connectionName, out DbConnection connection)
        {
            connection = null;
            if (scope.TryGetConnectionByName(connectionName, out connection))
            {
                return true;
            }
            else if (scope.TryGetCompatableScope(out scope))
            {
                if (this.TryGetConnectionByName(scope, connectionName, out connection))
                    return true;
                else
                    return false;
            }
            return false;
        }

        private bool TryGetConnectionByName(string connectionName, out DbConnection connection)
        {
            connection = null;
            lock (this.SyncRoot)
            {
                CheckDisposed();
                if (!_connections.IsValueCreated)
                    return false;
                return _connections.Value.TryGetValue(connectionName, out connection);
            }
        }

        /// <summary>
        /// Removes the connection from current scope
        /// and if not removed it tries to remove it from outer scopes
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        private bool TryRemoveConnection(DbConnection connection)
        {
            var scope = this;
            while (scope != null)
            {
                if (!scope._isDisposed && scope.TryRemoveConnectionInternal(connection))
                    return true;
                else
                    scope = scope._outerScope;
            }
            return false;
        }

        /// <summary>
        /// Removes the connection from current scope
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        private bool TryRemoveConnectionInternal(DbConnection connection)
        {
            lock (this.SyncRoot)
            {
                if (this._isDisposed)
                    return false;
                if (!_connections.IsValueCreated)
                    return false;
                string key = string.Empty;
                var connections = _connections.Value;
                foreach (var kvp in connections)
                {
                    if (Object.ReferenceEquals(kvp.Value, connection))
                    {
                        key = kvp.Key;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(key))
                {
                    DbConnection tmp;
                    if (connections.TryRemove(key, out tmp))
                    {
                        tmp.Dispose();
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Handle calling API function after instance has been disposed
        /// </summary>
        private void CheckDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException("DbConnectionScope");
            }
        }

        private void CheckTransaction()
        {
            string id = CurrentTransactionId;
            if (id  != this._transId)
            {
                throw new InvalidOperationException("Transaction is not the same when DbConnectionScope was created");
            }
        }

        private void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;
            if (disposing)
            {
                lock (this.SyncRoot)
                {
                    if (_isDisposed)
                        return;
                    DbConnectionScope outerScope = _outerScope;
                    while (outerScope != null && outerScope._isDisposed)
                    {
                        outerScope = outerScope._outerScope;
                    }

                    try
                    {
                        WeakReference<DbConnectionScope> tmp;
                        __scopeStore.TryRemove(this.UNIQUE_ID, out tmp);
                        __currentScope = outerScope;
                    }
                    finally
                    {
                        _isDisposed = true;
                        if (_connections.IsValueCreated)
                        {
                            var connections = _connections.Value.Values.ToArray();
                            _connections.Value.Clear();
                            foreach (DbConnection connection in connections)
                            {
                                if (connection.State != ConnectionState.Closed)
                                {
                                    connection.Dispose();
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                WeakReference<DbConnectionScope> tmp;
                __scopeStore.TryRemove(this.UNIQUE_ID, out tmp);
                _isDisposed = true;
            }
        }
#endregion
    }
}