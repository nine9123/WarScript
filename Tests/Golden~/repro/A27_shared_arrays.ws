# budget: 100000
# Plan A.27 (BUDGET-3, found reviewing T1.2): an array that holds the same
# array twice, 40 levels deep, is built in about 200 instructions but has
# 2^40 paths. v1 and T1.2 compared and formatted it once per path, so '=='
# and 'print' never returned, whatever the instruction budget. Equality now
# remembers the pairs it has found equal; formatting stops adding elements
# after WarValue.MaxFormattedLength characters and writes '...'.
a = {1}
b = {1}
loop i in 0..40
    a = {a, a}
    b = {b, b}
end
print a == b                             # expect: True
print a != b                             # expect: False
print Array_contains [{0, a}, b]         # expect: True
c = {2}
loop i in 0..40
    c = {c, c}
end
c{1} = 3
print a == c                             # expect: False
# (Formatting is checked in Tests/ValueDepthTests.cs: its text is longer
# than MaxTextLength, so a script cannot hold it.)

# The memo does not hide the depth limit: a pair found equal near the top
# still raises when it is met again deeper than WarValue.MaxValueDepth.
d = {}
e = {}
loop i in 0..199
    d = {d}
    e = {e}
end
# d is 200 arrays deep; its second copy below starts at depth 56, so its
# innermost array is at depth 255, the deepest allowed.
x = {d}
y = {e}
loop i in 0..54
    x = {x}
    y = {y}
end
print {d, x} == {e, y}                   # expect: True
# One level more and it is too deep, as without the memo.
x = {x}
y = {y}
x = {d, x}
y = {e, y}
begin
    print x == y
rescue err
    print err                            # expect: Value nested too deeply
end

# The state hash is not memoized yet (open part of BUDGET-3), so the shared
# arrays must not be left in globals.
a = 0
b = 0
c = 0
