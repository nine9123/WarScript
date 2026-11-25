# T3.8 (PROP-29): 'new C [...]' with another number of arguments than C's
# parameters is a compile error; v1 dropped the extra arguments and made
# the missing ones null.
class Unit(team, hp)
end
u = new Unit("red", 10, 3)   # expect-compile-error: Class 'Unit' takes 2 arguments, not 3
