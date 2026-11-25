# T3.8 (PROP-29): a lambda's parameters too.
twice = fun(x, x) return x * 2 end   # expect-compile-error: Parameter 'x' is declared twice
print twice(1, 2)
