# T3.4 (PROP-9): a name a function assigns is its local for the whole body,
# so reading it earlier in the source is an error, not a read of the global.
total = 0
fun add [n]
    total = total + n   # expect-compile-error: variable 'total' is read before it is assigned
end
