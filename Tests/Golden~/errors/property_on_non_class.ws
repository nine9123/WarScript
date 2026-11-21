# Reading a property of a value that is not an instance is a TypeError
# (plan T2.5).
# A class has such a field, so only the VM can tell (T3.8, PROP-29).
class Unit [hp]
end
x = 5
y = x :: hp   # expect-error: TypeError: Cannot access property 'hp' on non-class value
