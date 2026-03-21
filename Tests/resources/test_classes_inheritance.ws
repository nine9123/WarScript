# ──────────────────────────────────────────────────────
# test_classes_inheritance.ws
# Covers: single inheritance, multiple inheritance,
#         deep chains, method inheritance, property
#         pass-through, multi-parent, override-like
# ──────────────────────────────────────────────────────

# ── Single inheritance: property pass-through ──
class Animal [name]
end
class Dog [name] : Animal [name]
end
d = new Dog ["Rex"]
assert d :: name == "Rex"
assert d as Animal :: name == "Rex"

# ── Single inheritance: method inheritance ──
class Vehicle [speed]
    fun describe []
        return "speed: " + speed
    end
end
class Car [speed, doors] : Vehicle [speed]
end
car = new Car [120, 4]
assert car :: describe [] == "speed: 120"
assert car :: doors == 4

# ── Deep inheritance chain ──
class Base [a]
end
class Mid [a, b] : Base [a]
end
class Leaf [a, b, c] : Mid [a, b]
end
leaf = new Leaf [1, 2, 3]
assert leaf :: a == 1
assert leaf :: b == 2
assert leaf :: c == 3
assert leaf as Base :: a == 1
assert leaf as Mid :: a == 1
assert leaf as Mid :: b == 2

# ── Multiple inheritance ──
class Flyable [altitude]
    fun fly []
        return "flying at " + altitude
    end
end
class Swimmable [depth]
    fun swim []
        return "swimming at " + depth
    end
end
class Duck [altitude, depth] : Flyable [altitude], Swimmable [depth]
end
duck = new Duck [100, 5]
assert duck :: fly [] == "flying at 100"
assert duck :: swim [] == "swimming at 5"
assert duck as Flyable :: altitude == 100
assert duck as Swimmable :: depth == 5

# ── Multiple inheritance: property isolation ──
class Engine [horsepower]
end
class Wheels [count]
end
class Truck [hp, wheel_count] : Engine [hp], Wheels [wheel_count]
end
truck = new Truck [300, 6]
assert truck as Engine :: horsepower == 300
assert truck as Wheels :: count == 6

# ── Inheritance with methods using this ──
class Shape [type]
    fun get_type []
        return this :: type
    end
end
class Circle [radius, shape_type] : Shape [shape_type]
end
c = new Circle [5, "circle"]
assert c :: get_type [] == "circle"
assert c :: radius == 5

# ── Multiple instances of inherited class ──
dogs = {}
loop i in 0..4
    dogs << new Dog ["dog_" + i]
end
assert dogs{0} :: name == "dog_0"
assert dogs{1} :: name == "dog_1"
assert dogs{2} :: name == "dog_2"
assert dogs{3} :: name == "dog_3"

# ── Mutation through cast doesn't break original ──
class User [email]
end
class Person [name]
end
class Student [email, name] : User [email], Person [name]
end
s = new Student ["test@mail", "Alice"]
s as Person :: name = "Bob"
assert s :: name == "Bob"
assert s as Person :: name == "Bob"

# ── Multiple students share no state ──
s1 = new Student ["a@test", "Alice"]
s2 = new Student ["b@test", "Bob"]
s1 :: name = "Changed"
assert s1 :: name == "Changed"
assert s2 :: name == "Bob"

# ── Inherited constructor body ──
class Timestamped [created]
end
class Record [id, created] : Timestamped [created]
    label = "record_" + id
end
r = new Record [42, "2024-01-01"]
assert r :: label == "record_42"
assert r as Timestamped :: created == "2024-01-01"

# ── Three-level deep with methods at each level ──
class GrandParent [a]
    fun gp_method []
        return "gp: " + a
    end
end
class Parent [a, b] : GrandParent [a]
    fun p_method []
        return "p: " + b
    end
end
class Child [a, b, c] : Parent [a, b]
    fun c_method []
        return "c: " + c
    end
end
child = new Child [1, 2, 3]
assert child :: gp_method [] == "gp: 1"
assert child :: p_method [] == "p: 2"
assert child :: c_method [] == "c: 3"

# ── Alternating class types in a loop ──
class Cat [name] : Animal [name]
end

animals = {}
loop i in 0..3
    animals << new Dog ["dog_" + i]
    animals << new Cat ["cat_" + i]
end
assert animals{0} :: name == "dog_0"
assert animals{1} :: name == "cat_0"
assert animals{2} :: name == "dog_1"
assert animals{3} :: name == "cat_1"
assert animals{0} is Dog
assert animals{1} is Cat
assert animals{0} is Animal
assert animals{1} is Animal

# ── Inheritance chain: each level adds computed props ──
class WithArea [width, height]
    area = width * height
end
class WithVolume [width, height, depth] : WithArea [width, height]
    volume = width * height * depth
end
box = new WithVolume [3, 4, 5]
assert box :: volume == 60
# area lives in WithArea's scope — must cast to access it
assert box as WithArea :: area == 12
