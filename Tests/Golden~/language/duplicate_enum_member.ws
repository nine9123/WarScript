# T3.8 (PROP-29): an enum names each member once.
enum Element
    FIRE
    WATER
    FIRE   # expect-compile-error: Enum member 'FIRE' is already declared in enum 'Element'
end
print Element.FIRE
