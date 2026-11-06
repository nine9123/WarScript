# ──────────────────────────────────────────────────────
# test_deep_property_access.ws
# Deep property access tests: exercises Bug 4 fix
# (this :: prop{index}), chained :: access, nested
# property mutation, class method array access
# ──────────────────────────────────────────────────────

# ── 1. Basic this :: array_prop{index} ──
class Container []
    items = {10, 20, 30, 40, 50}

    fun get_item [i]
        return this :: items{i}
    end

    fun set_item [i, val]
        this :: items{i} = val
    end

    fun first []
        return this :: items{0}
    end

    fun last []
        n = 0
        loop x in this :: items
            n += 1
        end
        return this :: items{n - 1}
    end
end

c = new Container
assert c :: get_item [0] == 10
assert c :: get_item [4] == 50
assert c :: first [] == 10
assert c :: last [] == 50

c :: set_item [2, 99]
assert c :: get_item [2] == 99
assert c :: items == {10, 20, 99, 40, 50}

# ── 2. this :: string_prop{index} ──
class Message [text]
    fun char_at [i]
        return this :: text{i}
    end
end

m = new Message ["hello"]
assert m :: char_at [0] == "h"
assert m :: char_at [4] == "o"

# ── 3. Queue using this :: data{index} ──
class SimpleQueue []
    data = {}
    size = 0

    fun enqueue [val]
        this :: data << val
        this :: size += 1
    end

    fun peek []
        return this :: data{0}
    end

    fun dequeue []
        item = this :: data{0}
        new_data = {}
        loop i in 1..this :: size
            new_data << this :: data{i}
        end
        this :: data = new_data
        this :: size -= 1
        return item
    end
end

q = new SimpleQueue
q :: enqueue [100]
q :: enqueue [200]
q :: enqueue [300]
assert q :: peek [] == 100
assert q :: dequeue [] == 100
assert q :: dequeue [] == 200
assert q :: peek [] == 300
assert q :: size == 1

# ── 4. Chained :: property access (multi-level) ──
class Inner [value]
end
class Middle [inner_obj]
end
class Outer [middle_obj]
end

i = new Inner [42]
mid = new Middle [i]
out = new Outer [mid]
assert out :: middle_obj :: inner_obj :: value == 42

# ── 5. Chained :: with method calls ──
class NameHolder [name]
    fun get_name []
        return this :: name
    end
end
class Wrapper [holder]
    fun get_holder []
        return this :: holder
    end
end

nh = new NameHolder ["deep_value"]
w = new Wrapper [nh]
assert w :: get_holder [] :: get_name [] == "deep_value"
assert w :: holder :: name == "deep_value"
assert w :: holder :: get_name [] == "deep_value"

# ── 6. Modify nested property through chain ──
class Position [x, y]
end
class Entity [name, pos]
end

pos = new Position [10, 20]
entity = new Entity ["hero", pos]
assert entity :: pos :: x == 10
entity :: pos :: x = 50
assert entity :: pos :: x == 50
assert pos :: x == 50

# ── 7. Array of class instances — access through index + :: ──
class Item [label, qty]
end

inventory = {}
inventory << new Item ["sword", 1]
inventory << new Item ["shield", 2]
inventory << new Item ["potion", 10]

assert inventory{0} :: label == "sword"
assert inventory{1} :: qty == 2
assert inventory{2} :: label == "potion"

# Mutate through index
inventory{1} :: qty = 5
assert inventory{1} :: qty == 5

# ── 8. Class with array property, push and read back ──
class EventBus []
    events = {}

    fun emit [event_name, data]
        this :: events << event_name + ":" + data
    end

    fun last_event []
        n = 0
        loop e in this :: events
            n += 1
        end
        return this :: events{n - 1}
    end

    fun event_at [i]
        return this :: events{i}
    end

    fun count []
        n = 0
        loop e in this :: events
            n += 1
        end
        return n
    end
end

bus = new EventBus
bus :: emit ["click", "button_1"]
bus :: emit ["hover", "menu"]
bus :: emit ["click", "button_2"]

assert bus :: count [] == 3
assert bus :: event_at [0] == "click:button_1"
assert bus :: event_at [1] == "hover:menu"
assert bus :: last_event [] == "click:button_2"

# ── 9. Compound assignment on class property via :: ──
class Stats [hp, mp]
end

s = new Stats [100, 50]
s :: hp -= 30
s :: mp += 10
assert s :: hp == 70
assert s :: mp == 60

s :: hp *= 2
assert s :: hp == 140

s :: mp /= 3
assert s :: mp == 20

# ── 10. Multiple instances — indexed access is independent ──
players = {}
loop i in 0..4
    players << new Stats [100 + i * 10, 50]
end
assert players{0} :: hp == 100
assert players{1} :: hp == 110
assert players{2} :: hp == 120
assert players{3} :: hp == 130

players{0} :: hp = 0
assert players{0} :: hp == 0
assert players{1} :: hp == 110
