# An index without a value is a syntax error (it compiled to a null index,
# the TypeError "Index must be a number, not null", only once it ran).
a = [1, 2]
if false
    a[] = 3   # expect-compile-error: Empty index at line 5: an index takes one value (a[0])
end
