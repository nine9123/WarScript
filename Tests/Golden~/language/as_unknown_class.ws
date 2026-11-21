# T3.8: 'as' must name a class of the module set or the host (at runtime it
# would only answer null).
class Unit
end
fun cast [u]
    return u as Unti   # expect-compile-error: Class 'Unti' is not defined
end
