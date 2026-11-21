# Reading a property of a value that is not an instance is a TypeError
# (plan T2.5).
x = 5
y = x :: hp   # expect-error: TypeError: Cannot access property 'hp' on non-class value
