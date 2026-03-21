# ──────────────────────────────────────────────────────
# test_classes_cast_instanceof.ws
# Covers: is operator, as (cast) operator, multi-level
#         instanceof, cast + property read/write,
#         negative instanceof checks, all combinations
# ──────────────────────────────────────────────────────

# ── Basic instanceof ──
class Animal
end
class Mammal : Animal
end
class Cat : Mammal
end
class Reptile
end
class Lizard : Animal, Reptile
end

cat = new Cat
assert cat is Cat
assert cat is Mammal
assert cat is Animal
assert !(cat is Reptile)
assert !(cat is Lizard)

# ── Instanceof with multi-inheritance ──
lizard = new Lizard
assert lizard is Lizard
assert lizard is Reptile
assert lizard is Animal
assert !(lizard is Mammal)
assert !(lizard is Cat)

# ── Instanceof in conditions (pre-declare) ──
my_animal = new Cat
type_name = "unknown"
if my_animal is Cat
    type_name = "cat"
elif my_animal is Mammal
    type_name = "mammal"
else
    type_name = "unknown"
end
assert type_name == "cat"

# ── Instanceof in loop over heterogeneous array ──
animals = {}
animals << new Cat
animals << new Lizard
animals << new Cat
animals << new Lizard

cat_count = 0
lizard_count = 0
mammal_count = 0
loop a in animals
    if a is Cat
        cat_count += 1
    end
    if a is Lizard
        lizard_count += 1
    end
    if a is Mammal
        mammal_count += 1
    end
end
assert cat_count == 2
assert lizard_count == 2
assert mammal_count == 2

# ── Basic cast ──
class User [email]
end
class Person [name]
end
class Student [email, name] : User [email], Person [name]
end

student = new Student ["alice@test.com", "Alice"]
assert student :: email == "alice@test.com"
assert student :: name == "Alice"

# ── Cast to read base type property ──
assert student as User :: email == "alice@test.com"
assert student as Person :: name == "Alice"

# ── Cast to write base type property ──
student as Person :: name = "Bob"
assert student :: name == "Bob"
assert student as Person :: name == "Bob"

# ── Cast with derived property write ──
student :: email = "bob@test.com"
assert student :: email == "bob@test.com"
assert student as User :: email == "bob@test.com"

# ── Cast to same type is identity ──
assert student as Student :: email == "bob@test.com"

# ── Instanceof returns correct values ──
assert student is Student
assert student is User
assert student is Person

# ── Instanceof for unrelated types ──
assert !(student is Cat)
assert !(student is Animal)

# ── Multiple instances: cast isolation ──
s1 = new Student ["s1@test", "S1"]
s2 = new Student ["s2@test", "S2"]
s1 as Person :: name = "Changed"
assert s1 as Person :: name == "Changed"
assert s2 as Person :: name == "S2"

# ── Complex hierarchy cast ──
class HasId [id]
end
class HasName [name]
end
class HasEmail [email]
end
class FullUser [id, name, email] : HasId [id], HasName [name], HasEmail [email]
end
u = new FullUser [1, "Admin", "admin@test.com"]
assert u as HasId :: id == 1
assert u as HasName :: name == "Admin"
assert u as HasEmail :: email == "admin@test.com"
assert u is HasId
assert u is HasName
assert u is HasEmail
assert u is FullUser

# ── Cast with method calls ──
class Identifiable [id]
    fun get_id []
        return id
    end
end
class Describable [description]
    fun describe []
        return description
    end
end
class Item [id, description] : Identifiable [id], Describable [description]
end
item = new Item [42, "magic sword"]
assert item as Identifiable :: get_id [] == 42
assert item as Describable :: describe [] == "magic sword"

# ── Instanceof with deep chain ──
class A
end
class B : A
end
class C : B
end
class D : C
end
d_obj = new D
assert d_obj is D
assert d_obj is C
assert d_obj is B
assert d_obj is A

# ── Instanceof boolean in expression ──
result = cat is Animal and lizard is Animal
assert result

result2 = cat is Reptile or lizard is Reptile
assert result2

# ── Instanceof with negation ──
assert !(cat is Reptile)
assert !(cat is Lizard) and (lizard is Reptile)
