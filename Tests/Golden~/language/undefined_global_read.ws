# T3.8 (PROP-27): reading a name that no file of the module set writes and
# the host does not define can only raise NameError, so it is a compile error.
count = 1
print count + coutn   # expect-compile-error: undefined variable 'coutn'
