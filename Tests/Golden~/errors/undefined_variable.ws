# Reading a name that is neither a local nor defined at runtime is a NameError
# (DEC-5, 5.6, T3.5), not null. A name assigned null is defined. A name the
# module set never writes is a compile error (T3.8, PROP-27): these are written
# only on a path that never runs.
if false
    nowhere = 0
    missing = 0
end
n = null
print n
fun read_it() return nowhere end
begin
    read_it()
rescue e
    print e
end
x = missing + 1   # expect-error: NameError: undefined variable 'missing'
# expect: null
# expect: undefined variable 'nowhere'
