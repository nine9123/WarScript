# T3.5 (DEC-5, 5.6): a bare read of a name that is not defined raises NameError
# wherever it happens. A class body finds a field, then a global; a method
# finds only globals (fields need @x); an indexed write reads the name first.
g = "global"
class Unit [hp]
    seen = hp
    shared = g
    fun bare_field [] return hp end
    fun field [] return @hp end
end
u = new Unit [10]
print u :: seen
print u :: shared
print u :: field []
begin
    u :: bare_field []
rescue e
    print e
end
begin
    nothing{0} = 1
rescue e
    print e
end
class Broken []
    y = absent
end
begin
    b = new Broken
rescue e
    print e
end
# A lambda reads globals only.
f = fun [] return g end
print f []
h = fun [] return not_there end
begin
    h []
rescue e
    print e
end
# expect: 10
# expect: global
# expect: 10
# expect: undefined variable 'hp'
# expect: undefined variable 'nothing'
# expect: undefined variable 'absent'
# expect: global
# expect: undefined variable 'not_there'
