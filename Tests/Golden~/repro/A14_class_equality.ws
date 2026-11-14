# known-failing: SEM-8
# Plan A.14 (DEC-9, PROP-8): identity equality; a cast compares its root instance.
class P [x]
end
class Base [x]
end
class Derived [x] : Base [x]
end
print (new P [1]) == (new P [1])   # expect: false
p = new P [1]
print p == p                        # expect: true
d = new Derived [1]
print d == (d as Base)              # expect: true
