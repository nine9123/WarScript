# T3.8 (PROP-29): 'obj :: x' where x is neither a field nor a method of any
# class of the module set or the host is a compile error (at runtime it is
# a NameError: errors/undeclared_property_read.ws).
class Unit [hp]
end
u = new Unit [10]
print u :: hpp   # expect-compile-error: 'hpp' is not a field or method of any class
