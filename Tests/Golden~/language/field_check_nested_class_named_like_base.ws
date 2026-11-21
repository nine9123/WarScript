# T3.4 review: a class with a nested class named like one of its bases.
# Which class the base is depends on where the instance is created (the VM
# resolves a base class from the scope of the `new`), so the compile-time
# field check cannot know the base's fields and checks nothing for D: `@b`,
# a field of the top-level B, compiles. (Before the fix the check looked
# the base up through D's own scope, found the nested B and rejected `@b`.)
class B [x]
    b = 1
end
class D [x] : B [x]
    class B [q]
    end
    fun f [] return @b end
end
print "compiled"
# expect: compiled
