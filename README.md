# DbConnectionScope
DbConnectionScope working normaly in async await environment

This is a version of DbConnectionScope that was introduced to the public in MSDN in 2006 (http://blogs.msdn.com/b/dataaccess/archive/2006/02/14/532026.aspx).
The problem with that version that it does not work properly in todays async environment of the Net Framework.
I upgraded that version to work normarly in this case and it uses CallContext instead ThreadStatic variable to store
the context.
Use it as you like!
