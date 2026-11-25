# T3.8: an enum member, or an enum's 'count', named like a constant is a field
# of the enum's class, not a write of the constant: it compiles.
const RED = 7
const count = 9
enum Color
    RED
    GREEN
end
print Color.RED
print Color.count
print RED
print count
# expect: 0
# expect: 2
# expect: 7
# expect: 9
