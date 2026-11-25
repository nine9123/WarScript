# Instantiating a class that does not exist is a NameError (plan T2.5).
# 'new Nope []' is already a compile error (T3.8, PROP-29:
# language/new_undefined_class.ws); a nested class is looked up in the
# instance's class when 'new' runs.
class Unit
end
unit = new Unit()
u = unit.new Nope()   # expect-error: NameError: Class 'Nope' is not defined
