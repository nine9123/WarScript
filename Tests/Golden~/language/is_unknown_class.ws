# T3.8: 'is' must name a class of the module set or the host (at runtime it
# would only answer false).
class Unit
end
u = new Unit()
print u is Unti   # expect-compile-error: Class 'Unti' is not defined
