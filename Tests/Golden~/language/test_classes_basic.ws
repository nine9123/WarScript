# ──────────────────────────────────────────────────────
# test_classes_basic.ws
# Covers: class definition, properties, constructors,
#         methods, this, property mutation, class body
#         statements, methods with params, nested calls
# ──────────────────────────────────────────────────────

# ── Basic class with properties ──
class Point [x, y]
end
p = new Point [3, 4]
assert p :: x == 3
assert p :: y == 4

# ── Class with no properties ──
class Empty
end
e = new Empty
assert e is Empty

# ── Class with default property via body ──
class Config [name]
    version = 1
    active = true
end
cfg = new Config ["test"]
assert cfg :: name == "test"
assert cfg :: version == 1
assert cfg :: active == true

# ── Constructor body with computation ──
class Pair [first, second]
    sum = first + second
    product = first * second
end
pair = new Pair [3, 7]
assert pair :: sum == 10
assert pair :: product == 21

# ── Property mutation ──
class Box [value]
end
b = new Box [10]
assert b :: value == 10
b :: value = 20
assert b :: value == 20
b :: value = b :: value + 5
assert b :: value == 25

# ── Method definition and call ──
class Counter [n]
    fun increment []
        n = n + 1
    end
    fun get []
        return n
    end
end
c = new Counter [0]
c :: increment []
c :: increment []
c :: increment []
assert c :: get [] == 3

# ── Method with parameters ──
class Calculator []
    fun add [a, b]
        return a + b
    end
    fun multiply [a, b]
        return a * b
    end
end
calc = new Calculator
assert calc :: add [3, 4] == 7
assert calc :: multiply [5, 6] == 30

# ── Method using this ──
class Entity [name, hp]
    fun is_alive []
        return this :: hp > 0
    end
    fun take_damage [amount]
        this :: hp = this :: hp - amount
    end
    fun status []
        return this :: name + ": " + this :: hp
    end
end
hero = new Entity ["Hero", 100]
assert hero :: is_alive []
assert hero :: status [] == "Hero: 100"
hero :: take_damage [30]
assert hero :: hp == 70
assert hero :: status [] == "Hero: 70"

# ── Multiple instances are independent ──
a = new Point [1, 2]
b = new Point [3, 4]
a :: x = 99
assert a :: x == 99
assert b :: x == 3

# ── Instance independence with methods ──
c1 = new Counter [0]
c2 = new Counter [100]
c1 :: increment []
c1 :: increment []
c1 :: increment []
c1 :: increment []
c1 :: increment []
assert c1 :: get [] == 5
assert c2 :: get [] == 100

# ── Creating instances in a loop ──
points = {}
loop i in 0..5
    points << new Point [i, i * 10]
end
assert points{0} :: x == 0
assert points{0} :: y == 0
assert points{1} :: x == 1
assert points{1} :: y == 10
assert points{4} :: x == 4
assert points{4} :: y == 40

# ── Method returning class instance ──
class Wrapper [inner]
    fun get_inner []
        return this :: inner
    end
end
inner_point = new Point [5, 10]
w = new Wrapper [inner_point]
result = w :: get_inner []
assert result :: x == 5
assert result :: y == 10

# ── Class method calling another method ──
class Stats [base_atk]
    fun buffed_atk [multiplier]
        return this :: get_atk [] * multiplier
    end
    fun get_atk []
        return base_atk
    end
end
s = new Stats [10]
assert s :: get_atk [] == 10
assert s :: buffed_atk [3] == 30

# ── Class with array property ──
class Inventory []
    items = {}
    fun add_item [item]
        this :: items << item
    end
    fun count []
        n = 0
        loop i in this :: items
            n += 1
        end
        return n
    end
end
inv = new Inventory
inv :: add_item ["sword"]
inv :: add_item ["shield"]
inv :: add_item ["potion"]
assert inv :: count [] == 3
assert inv :: items == {"sword", "shield", "potion"}

# ── Null property default ──
class OptionalField [required]
end
obj = new OptionalField ["yes"]
assert obj :: required == "yes"

# ── Chained property access ──
class Outer [inner_obj]
end
inner = new Point [7, 8]
outer = new Outer [inner]
assert outer :: inner_obj :: x == 7
assert outer :: inner_obj :: y == 8

# ── Class with method using loop ──
class Summation []
    fun sum_to [n]
        total = 0
        loop i in 0..n + 1
            total += i
        end
        return total
    end
end
sm = new Summation
assert sm :: sum_to [10] == 55
assert sm :: sum_to [0] == 0
assert sm :: sum_to [1] == 1
