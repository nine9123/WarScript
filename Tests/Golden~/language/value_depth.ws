# T1.2: equality and formatting are cycle-safe and depth-limited
# (MaxValueDepth = 256, PROP-5). Formatting prints an array that is already
# being formatted, or one nested deeper than the limit, as [...]; equality
# deeper than the limit raises the rescuable LimitExceeded
# "Value nested too deeply" (CRASH-1).

# Cycles format as [...], at any position.
a = {1, 2}
a << a
print a                                  # expect: [1, 2, [...]]
b = {a, 3}
print b                                  # expect: [[1, 2, [...]], 3]

# The same array twice side by side is not a cycle.
s = {7}
print {s, s}                             # expect: [[7], [7]]

# 10,000 levels: formatting stops at depth 256 (text + array makes an
# array, so the formatted text is checked through print).
deep = {}
loop i in 0..10000
    deep = {deep}
end
# (256 opening brackets, [...], 256 closing ones)
print deep
# expect: [[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[...]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]

# Equality within the limit compares structurally...
x = {}
y = {}
loop i in 0..255
    x = {x}
    y = {y}
end
print x == y                             # expect: True
y = {}
loop i in 0..255
    y = {y, 1}
end
print x == y                             # expect: False

# ...and one level more is an error, for '==', '!=' and the array natives.
x = {x}
y = {}
loop i in 0..256
    y = {y}
end
begin
    print x == y
rescue e
    print "==: " + e                     # expect: ==: Value nested too deeply
end
begin
    print deep != deep
rescue e
    print "!=: " + e                     # expect: !=: Value nested too deeply
end
c = {1}
c << c
begin
    print Array_contains [c, c]
rescue e
    print "contains: " + e               # expect: contains: Value nested too deeply
end
begin
    print Array_index_of [c, c]
rescue e
    print "index_of: " + e               # expect: index_of: Value nested too deeply
end
begin
    print Array_remove [c, c]
rescue e
    print "remove: " + e                 # expect: remove: Value nested too deeply
end
print Array_contains [c, 1]              # expect: True

# Instances count as a level too.
class Box [v]
end
p = new Box [1]
q = new Box [1]
loop i in 0..300
    p = new Box [p]
    q = new Box [q]
end
begin
    print p == q
rescue e
    print "instances: " + e              # expect: instances: Value nested too deeply
end
