# T3.8 (PROP-29): top-level code is outside every class.
print this   # expect-compile-error: 'this' and '@' fields can only be used inside a class
