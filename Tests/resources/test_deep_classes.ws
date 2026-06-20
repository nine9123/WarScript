# ──────────────────────────────────────────────────────
# test_deep_classes.ws
# Deep class tests: composition, builder pattern,
# method returning this, classes storing classes,
# polymorphic dispatch via instanceof, complex
# multi-inheritance method resolution
# ──────────────────────────────────────────────────────

# ── 1. Composition: class holding another class ──
class Engine [horsepower]
    fun describe []
        return "Engine: " + horsepower + "hp"
    end
end

class Car [make, engine]
    fun full_description []
        return make + " with " + this :: engine :: describe []
    end
end

e = new Engine [200]
c = new Car ["Tesla", e]
assert c :: full_description [] == "Tesla with Engine: 200hp"
assert c :: engine :: horsepower == 200

# ── 2. Builder pattern ──
class QueryBuilder []
    table_name = ""
    where_clause = ""
    limit_val = 0

    fun from [table]
        this :: table_name = table
    end

    fun where [condition]
        if this :: where_clause == ""
            this :: where_clause = condition
        else
            this :: where_clause = this :: where_clause + " AND " + condition
        end
    end

    fun limit [n]
        this :: limit_val = n
    end

    fun build []
        result = "SELECT * FROM " + this :: table_name
        if this :: where_clause != ""
            result = result + " WHERE " + this :: where_clause
        end
        if this :: limit_val > 0
            result = result + " LIMIT " + this :: limit_val
        end
        return result
    end
end

qb = new QueryBuilder
qb :: from ["users"]
qb :: where ["age > 18"]
qb :: where ["active = true"]
qb :: limit [10]
assert qb :: build [] == "SELECT * FROM users WHERE age > 18 AND active = true LIMIT 10"

qb2 = new QueryBuilder
qb2 :: from ["products"]
assert qb2 :: build [] == "SELECT * FROM products"

# ── 3. Polymorphic dispatch via instanceof ──
class Shape [type]
end
class Rectangle [w, h, shape_type] : Shape [shape_type]
end
class Circle [r, shape_type] : Shape [shape_type]
end
class Triangle [base_len, height, shape_type] : Shape [shape_type]
end

fun compute_area [shape]
    if shape is Rectangle
        rect = shape as Rectangle
        return rect :: w * rect :: h
    elif shape is Circle
        circ = shape as Circle
        return 3.14159 * circ :: r * circ :: r
    elif shape is Triangle
        tri = shape as Triangle
        return tri :: base_len * tri :: height / 2
    end
    return 0
end

shapes = {}
shapes << new Rectangle [5, 3, "rectangle"]
shapes << new Circle [10, "circle"]
shapes << new Triangle [6, 4, "triangle"]

assert compute_area [shapes{0}] == 15
assert floor [compute_area [shapes{1}]] == 314
assert compute_area [shapes{2}] == 12

# ── 4. Total area of all shapes ──
total = 0
loop s in shapes
    total += compute_area [s]
end
assert total > 340

# ── 5. Class with class factory method ──
class Vector2 [x, y]
    fun add [other]
        return new Vector2 [x + other :: x, y + other :: y]
    end
    fun scale [factor]
        return new Vector2 [x * factor, y * factor]
    end
    fun magnitude_squared []
        return x * x + y * y
    end
    fun equals [other]
        return x == other :: x and y == other :: y
    end
end

v1 = new Vector2 [3, 4]
v2 = new Vector2 [1, 2]
v3 = v1 :: add [v2]
assert v3 :: x == 4
assert v3 :: y == 6

v4 = v1 :: scale [2]
assert v4 :: x == 6
assert v4 :: y == 8

assert v1 :: magnitude_squared [] == 25
assert v1 :: equals [new Vector2 [3, 4]]
assert !(v1 :: equals [v2])

# ── 6. Chain of method calls (via intermediate vars) ──
origin = new Vector2 [0, 0]
step1 = origin :: add [new Vector2 [5, 0]]
step2 = step1 :: add [new Vector2 [0, 5]]
step3 = step2 :: scale [3]
assert step3 :: x == 15
assert step3 :: y == 15

# ── 7. Class with state machine ──
class Counter [start]
    value = start
    history = {}

    fun increment [amount]
        this :: value += amount
        this :: history << this :: value
    end

    fun decrement [amount]
        this :: value -= amount
        this :: history << this :: value
    end

    fun reset []
        this :: value = 0
        this :: history << 0
    end
end

ct = new Counter [10]
ct :: increment [5]
ct :: increment [3]
ct :: decrement [2]
ct :: reset []
ct :: increment [100]
assert ct :: value == 100
assert ct :: history == {15, 18, 16, 0, 100}

# ── 8. Multi-level inheritance with method at each level ──
class Lifeform [name]
    fun identify []
        return "Lifeform: " + name
    end
end
class Animal [name, legs] : Lifeform [name]
    fun move []
        return name + " moves on " + legs + " legs"
    end
end
class Pet [name, legs, owner] : Animal [name, legs]
    fun greet []
        return name + " belongs to " + owner
    end
end

pet = new Pet ["Buddy", 4, "Alice"]
assert pet :: identify [] == "Lifeform: Buddy"
assert pet :: move [] == "Buddy moves on 4 legs"
assert pet :: greet [] == "Buddy belongs to Alice"
assert pet is Pet
assert pet is Animal
assert pet is Lifeform

# ── 9. Class storing array of class instances ──
class Team [name]
    members = {}

    fun add_member [member]
        this :: members << member
    end

    fun size []
        count = 0
        loop m in this :: members
            count += 1
        end
        return count
    end

    fun find_by_name [target_name]
        loop m in this :: members
            if m :: name == target_name
                return m
            end
        end
        return null
    end
end

class Member [name, role]
end

team = new Team ["Dev Team"]
team :: add_member [new Member ["Alice", "lead"]]
team :: add_member [new Member ["Bob", "dev"]]
team :: add_member [new Member ["Charlie", "qa"]]

assert team :: size [] == 3
assert team :: find_by_name ["Bob"] :: role == "dev"
assert team :: find_by_name ["Alice"] :: role == "lead"
assert team :: find_by_name ["Unknown"] == null

# ── 10. Multiple inheritance method from each parent ──
class JsonSerializable [data]
    fun to_json []
        return "data=" + data
    end
end
class Printable [label]
    fun to_display []
        return "[" + label + "]"
    end
end
class LogEntry [data, label, timestamp] : JsonSerializable [data], Printable [label]
    fun full_log []
        return this :: to_display [] + " " + timestamp + " " + this :: to_json []
    end
end

entry = new LogEntry ["error occurred", "ERROR", "2024-01-15"]
assert entry :: to_json [] == "data=error occurred"
assert entry :: to_display [] == "[ERROR]"
assert entry :: full_log [] == "[ERROR] 2024-01-15 data=error occurred"
assert entry is JsonSerializable
assert entry is Printable
assert entry is LogEntry
