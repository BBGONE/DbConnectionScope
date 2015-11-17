# DbConnectionScope
<b>DbConnectionScope working normaly in async await environment</b>

This is a version of DbConnectionScope that was introduced to the public in MSDN in 2006 (http://blogs.msdn.com/b/dataaccess/archive/2006/02/14/532026.aspx).
The problem with that version that it does not work properly in todays async environment of the Net Framework.
I upgraded that version to work normarly in this case and it uses CallContext instead ThreadStatic variable to store
the context.<br/>
Every transaction will get each own connection even if the scope already have a connection which was created in another transaction.
The connection will be reused if transaction is the same (suppressed transaction is treated like it was a transaction).
<br/><br/>
LICENCE: Use it as you like!
