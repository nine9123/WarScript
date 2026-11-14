# Calling a method on a value that is not an instance is a TypeError
# (plan T2.5).
x = 5
x :: hit [1]   # expect-error: TypeError: Cannot call method 'hit' on non-class value
