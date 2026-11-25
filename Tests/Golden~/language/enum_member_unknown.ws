# T3.8 (PROP-29): 'E.X' where E is an enum of the module set and X is
# none of its members, 'values', 'names', 'count' or 'name' is a compile
# error, even when another class has a field X.
enum DamageType
    PHYSICAL
    MAGICAL
end
class Spell(element)
end
print DamageType.element   # expect-compile-error: 'element' is not a member of enum 'DamageType'
