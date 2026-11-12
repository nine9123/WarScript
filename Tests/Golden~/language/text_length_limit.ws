# T1.4: text operators stop at MaxTextLength (default 1,048,576 characters)
# with a rescuable "Text too long", checked before anything is allocated.
at = "a" * 1048576
print "repeat at the limit ok"           # expect: repeat at the limit ok
begin
    x = "a" * 1048577
rescue e
    print "repeat: {e}"                  # expect: repeat: Text too long
end
begin
    x = at + "b"
rescue e
    print "concat: {e}"                  # expect: concat: Text too long
end
begin
    x = at
    x{0} = "bc"
rescue e
    print "index write: {e}"             # expect: index write: Text too long
end
half = "a" * 524288
y = half + half
assert y == at
print "concat at the limit ok"           # expect: concat at the limit ok
