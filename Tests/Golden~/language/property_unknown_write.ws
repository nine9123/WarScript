# T3.8 (PROP-29): writing 'obj :: x' that no class declares is a compile
# error too, like an index write into it.
class Unit [hp]
end
u = new Unit [10]
u :: armor = 3   # expect-compile-error: 'armor' is not a field or method of any class
