# ──────────────────────────────────────────────────────
# test_nested_classes.ws
# Covers: :: new (nested class instantiation), nested
#         class with properties, methods on nested,
#         chained access through nested objects
# ──────────────────────────────────────────────────────

# ── Nested class definition and instantiation ──
class Outer []
    class Inner [value]
    end
end

host = new Outer
nested = host :: new Inner [42]
assert nested :: value == 42

# ── Nested class with method ──
class Factory []
    class Product [name, price]
        fun describe []
            return name + ": $" + price
        end
    end
end

factory = new Factory
item = factory :: new Product ["Widget", 9.99]
assert item :: name == "Widget"
assert item :: price == 9.99
assert item :: describe [] == "Widget: $9.99"

# ── Multiple nested instances ──
items = {}
loop i in 0..3
    items << factory :: new Product ["item_" + i, i * 10]
end
assert items{0} :: name == "item_0"
assert items{0} :: price == 0
assert items{1} :: name == "item_1"
assert items{1} :: price == 10
assert items{2} :: name == "item_2"
assert items{2} :: price == 20

# ── Nested class in a class with state ──
class GameWorld []
    entity_count = 0

    class Entity [name]
    end

    fun spawn [name]
        entity_count += 1
        return this :: new Entity [name]
    end
end

world = new GameWorld
e1 = world :: spawn ["hero"]
e2 = world :: spawn ["villain"]
assert e1 :: name == "hero"
assert e2 :: name == "villain"
assert world :: entity_count == 2

# ── Deep property access through nested objects ──
class Tree []
    class Node [value, left, right]
    end
end

t = new Tree
root = t :: new Node [10, null, null]
left = t :: new Node [5, null, null]
right = t :: new Node [15, null, null]
root :: left = left
root :: right = right
assert root :: value == 10
assert root :: left :: value == 5
assert root :: right :: value == 15

# ── Nested class independence ──
class Container []
    class Item [id]
    end
end

c1 = new Container
c2 = new Container
i1 = c1 :: new Item [1]
i2 = c2 :: new Item [2]
assert i1 :: id == 1
assert i2 :: id == 2
i1 :: id = 99
assert i1 :: id == 99
assert i2 :: id == 2
