# Plan A.13 (DEC-10). v1 ignores the write.
class Unit(hp)
end
x = null
x.hp = 5       # expect-error: TypeError
