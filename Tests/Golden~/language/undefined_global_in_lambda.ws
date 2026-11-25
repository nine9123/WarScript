# T3.8 (PROP-27): a lambda reads globals only.
f = fun(x) return x * scale end   # expect-compile-error: undefined variable 'scale'
