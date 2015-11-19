# DbConnectionScope
<b>DbConnectionScope working normaly in async await environment</b>

This is a version of DbConnectionScope that was introduced to the public in MSDN in 2006 (http://blogs.msdn.com/b/dataaccess/archive/2006/02/14/532026.aspx).
The problem with that version that it does not work properly in todays async environment of the Net Framework.
I upgraded that version to work normarly in this case and it uses CallContext instead ThreadStatic variable to store
the context.<br/>
Every transaction will get each own connection even if the scope already have a connection which was created in another transaction.
The connection will be reused if transaction is the same (suppressed transaction is treated like it was a transaction).<br/>
But if you are reusing a connection on multiple threads (SqlConnection allows it) then your queries will be serialized one after another.<br/>
So if you are using <b>TransactionScopeOption.Suppress</b> and share this connection between parrallelly executing threads then it is better
to use DbConnectionScopeOption.RequiresNew in  this case  to create new connection for each thread.<br/><br/>
For transactions - if you use TransactionScopeOption.Required, then use DbConnectionScopeOption.Required to reuse the same connection.<br/>
<b>Don't combine TransactionScopeOption.Required with DbConnectionScopeOption.RequiresNew</b> - because it can end in distributed 
transaction!<br/>
Another safe combination is TransactionScopeOption.RequiresNew and DbConnectionScopeOption.Required - it will execute your query on 
a new transaction and a new connection.
<br/><br/>
LICENCE: Use it as you like!
