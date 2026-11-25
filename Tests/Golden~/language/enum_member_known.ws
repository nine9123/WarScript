# T3.8 (PROP-29): the members of an enum, 'values', 'names', 'count' and
# 'name(v)' compile; a local named like an enum is not the enum.
enum DamageType
    PHYSICAL
    MAGICAL
    TRUE = 5
end
class Spell(element)
end
print DamageType.MAGICAL                          # expect: 1
print DamageType.name(5)                         # expect: TRUE
print DamageType.count                            # expect: 3
print DamageType.values                           # expect: [0, 1, 5]
print DamageType.names                            # expect: [PHYSICAL, MAGICAL, TRUE]
fun element_of(DamageType)
    return DamageType.element
end
print element_of(new Spell("fire"))                # expect: fire
