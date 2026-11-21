# T3.8 (PROP-29): 'new' naming no class of the module set or the host is a
# compile error (at runtime it was a NameError: errors/undefined_class.ws).
class Unit
end
u = new Unti []   # expect-compile-error: Class 'Unti' is not defined
