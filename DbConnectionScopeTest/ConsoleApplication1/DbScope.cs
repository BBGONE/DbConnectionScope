using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Bell.PPS.Database.Shared
{
    public class DbScope : IDisposable
    {
        private TransactionScope _tranScope;
        private DbConnectionScope _connScope;
        private bool _disposed;

        public DbScope() :
            this(TransactionScopeOption.Required, IsolationLevel.Serializable, TimeSpan.FromSeconds(30))
        {
        }

        public DbScope(TransactionScopeOption option) :
            this(option, IsolationLevel.Serializable, TimeSpan.FromSeconds(30))
        {
        }

        public DbScope(TransactionScopeOption option, IsolationLevel isolationLevel):
            this(option, isolationLevel, TimeSpan.FromSeconds(30))
        {
        }

        public DbScope(TransactionScopeOption option, TimeSpan timeOut) :
            this(option, IsolationLevel.Serializable, timeOut)
        {
        }

        public DbScope(TimeSpan timeOut) :
           this(TransactionScopeOption.Required, IsolationLevel.Serializable, timeOut)
        {
        }
        public DbScope(TransactionScopeOption option, IsolationLevel isolationLevel, TimeSpan timeOut)
        {
            _disposed = true;
            if (option != TransactionScopeOption.Suppress )
            {
                TransactionOptions tranOp = new TransactionOptions() { IsolationLevel = isolationLevel, Timeout = timeOut };
                _tranScope = new TransactionScope(option, tranOp, TransactionScopeAsyncFlowOption.Enabled);
                _connScope = new DbConnectionScope(option == TransactionScopeOption.RequiresNew? DbConnectionScopeOption.RequiresNew: DbConnectionScopeOption.Required);
            }
            else
            {
                _tranScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
                _connScope = new DbConnectionScope(DbConnectionScopeOption.RequiresNew);
            }
            _disposed = false;
        }

        ~DbScope()
        {
            Dispose(false);
        }

        public void Complete()
        {
            this.CheckDisposed();
            _tranScope.Complete();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("UnitOfWork");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            try
            {
                if (disposing)
                {
                    using (_tranScope)
                    using (_connScope)
                    {
                    }
                }
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
