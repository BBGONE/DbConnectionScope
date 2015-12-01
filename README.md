# DbConnectionScope
<b>DbConnectionScope working normaly in async await environment</b>

This is a version of DbConnectionScope that was introduced to the public in MSDN in 2006 (http://blogs.msdn.com/b/dataaccess/archive/2006/02/14/532026.aspx).
The problem with that version that it does not work properly in todays async environment of the Net Framework.
I upgraded that version to work normally in this case and it uses CallContext instead ThreadStatic variable to store
the context.<br/>
You need to create a new connection scope for each new transaction scope, because the scope is  bound to the transaction.
The scope is not bound to a thread (like the original version) - because it was designed to work in async environment.<br/>
The connection will be reused if transaction is the same (suppressed transaction is treated like it was a transaction).<br/>
But if you are reusing a connection on multiple threads (SqlConnection allows it with MultipleActiveResultSets=True) then your queries executions will be serialized one after another.<br/>
So if you are using <b>TransactionScopeOption.Suppress</b> and using the connection in parrallel executing threads then it is better
to create a new connection scope foreach thread using DbConnectionScopeOption.RequiresNew in  this case.<br/><br/>
For transaction scope - if you use TransactionScopeOption.Required, then use DbConnectionScopeOption.Required to reuse the same connection.
It is  needed because if you create a new connection in the same transaction then you will promote the transaction to the distributed one.
And it is the main reason to use DbConnectionScope in your applications - To prevent distributed transactions.
<br/>
<b>Don't combine TransactionScopeOption.Required with DbConnectionScopeOption.RequiresNew</b> - because it can end in distributed 
transaction!<br/>
Another safe combination is TransactionScopeOption.RequiresNew and DbConnectionScopeOption.Required - it will execute your query on 
a new transaction and a new connection.
<br/><br/>
I introduced a new class DbScope which combines the TransactionScope and DbConnectionScope in one vial. It is easier to use than those two classes.
<br/><br/>
LICENSE: Use it as you like!
