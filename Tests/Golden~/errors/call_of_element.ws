# T3.12 review: an element of an array cannot be called directly either.
fs = [fun(x) return x + 100 end]
print fs[0](5)   # expect-compile-error: only a name can be called: put a call's or an element's result in a variable first
