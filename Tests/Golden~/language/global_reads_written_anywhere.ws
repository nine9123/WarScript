# T3.8 (PROP-27): a read compiles when any part of the module set writes the
# name as a global: later at the top level, through 'global' in a function, as
# a const or an enum, or only on a path that does not run (which then raises
# NameError at runtime). A class body also finds fields, its own or those of
# a class deriving from it (one field scope per instance).
fun report []
    return later + counter
end
fun bump []
    global counter
    counter = 1
end
class Base []
    seen = extra
end
class Derived [extra] : Base []
end
later = 10
bump []
print report []          # expect: 11
fun limit []
    return MAX + Kind :: B
end
const MAX = 5
enum Kind
    A
    B
end
print limit []           # expect: 6
print (new Derived [7]) :: seen   # expect: 7
if false
    never = 0
end
begin
    print never
rescue e
    print e              # expect: undefined variable 'never'
end
