# ──────────────────────────────────────────────────────
# test_integration.ws
# Integration tests combining multiple language features
# ──────────────────────────────────────────────────────

# ── 1. Linked list implementation ──
class ListNode [value, next_node]
end

fun list_push [head, val]
    return new ListNode [val, head]
end

fun list_to_array [head]
    result = {}
    current = head
    loop current != null
        result << current :: value
        current = current :: next_node
    end
    return result
end

fun list_length [head]
    count = 0
    current = head
    loop current != null
        count += 1
        current = current :: next_node
    end
    return count
end

head = null
head = list_push [head, 3]
head = list_push [head, 2]
head = list_push [head, 1]

assert list_length [head] == 3
assert list_to_array [head] == {1, 2, 3}
assert head :: value == 1
assert head :: next_node :: value == 2
assert head :: next_node :: next_node :: value == 3

# ── 2. State machine ──
class StateMachine [current_state]
    transitions = {}

    fun add_transition [from_state, to_state]
        this :: transitions << from_state + "->" + to_state
    end

    fun can_transition [to_state]
        key = this :: current_state + "->" + to_state
        loop t in this :: transitions
            if t == key
                return true
            end
        end
        return false
    end

    fun go [to_state]
        if this :: can_transition [to_state]
            this :: current_state = to_state
            return true
        end
        return false
    end
end

sm = new StateMachine ["idle"]
sm :: add_transition ["idle", "walking"]
sm :: add_transition ["walking", "running"]
sm :: add_transition ["running", "idle"]

assert sm :: current_state == "idle"
assert sm :: can_transition ["walking"]

# Bug 2 fix: !obj :: method now parses as !(obj :: method)
assert !sm :: can_transition ["running"]

assert sm :: go ["walking"]
assert sm :: current_state == "walking"

assert sm :: go ["running"]
assert sm :: current_state == "running"

assert !sm :: go ["walking"]
assert sm :: current_state == "running"

assert sm :: go ["idle"]
assert sm :: current_state == "idle"

# ── 3. Accumulator with exception handling ──
class Accumulator [initial]
    value = initial

    fun add [n]
        this :: value += n
    end

    fun subtract [n]
        if n > this :: value
            raise new OverdrawError [n, this :: value]
        end
        this :: value -= n
    end

    fun get []
        return this :: value
    end
end

class OverdrawError [requested, available]
end

acc = new Accumulator [100]
acc :: add [50]
assert acc :: get [] == 150

acc :: subtract [30]
assert acc :: get [] == 120

overdraw_caught = false
overdraw_req = 0
overdraw_avail = 0
begin
    acc :: subtract [200]
rescue e
    overdraw_caught = true
    assert e is OverdrawError
    overdraw_req = e :: requested
    overdraw_avail = e :: available
end
assert overdraw_caught
assert overdraw_req == 200
assert overdraw_avail == 120
assert acc :: get [] == 120

# ── 4. String builder pattern ──
class StringBuilder []
    parts = {}

    fun append [text]
        this :: parts << text
        return this
    end

    fun build [separator]
        result = ""
        count = 0
        loop p in this :: parts
            if count > 0
                result += separator
            end
            result += p
            count += 1
        end
        return result
    end
end

sb = new StringBuilder
sb :: append ["hello"]
sb :: append ["beautiful"]
sb :: append ["world"]
assert sb :: build [" "] == "hello beautiful world"
assert sb :: build [", "] == "hello, beautiful, world"
assert sb :: build ["-"] == "hello-beautiful-world"

# ── 5. Multiple inheritance with method interaction ──
class Printable [label]
    fun to_str []
        return "{label}"
    end
end

class Serializable [data]
    fun serialize []
        return "data:" + data
    end
end

class Record [label, data] : Printable [label], Serializable [data]
    fun full_repr []
        return this :: to_str [] + " | " + this :: serialize []
    end
end

r = new Record ["item_1", "payload"]
assert r :: to_str [] == "item_1"
assert r :: serialize [] == "data:payload"
assert r :: full_repr [] == "item_1 | data:payload"
assert r is Printable
assert r is Serializable
assert r is Record

# ── 6. Array of class instances with filtering ──
class Item [name, price, category]
end

items = {}
items << new Item ["Sword", 100, "weapon"]
items << new Item ["Shield", 80, "armor"]
items << new Item ["Potion", 20, "consumable"]
items << new Item ["Bow", 120, "weapon"]
items << new Item ["Helmet", 60, "armor"]
items << new Item ["Bomb", 30, "consumable"]

fun filter_by_category [items, category]
    result = {}
    loop item in items
        if item :: category == category
            result << item
        end
    end
    return result
end

fun total_price [items]
    sum = 0
    loop item in items
        sum += item :: price
    end
    return sum
end

weapons = filter_by_category [items, "weapon"]
armor = filter_by_category [items, "armor"]
consumables = filter_by_category [items, "consumable"]

assert total_price [weapons] == 220
assert total_price [armor] == 140
assert total_price [consumables] == 50
assert total_price [items] == 410

# ── 7. Complex interpolation + computation ──
class Stats [hp, atk, def]
    fun effective_damage [target_def]
        dmg = this :: atk - target_def
        if dmg < 0
            return 0
        end
        return dmg
    end

    fun summary []
        return "HP:{this :: hp} ATK:{this :: atk} DEF:{this :: def}"
    end
end

hero_stats = new Stats [100, 25, 15]
enemy_stats = new Stats [50, 18, 10]

assert hero_stats :: summary [] == "HP:100 ATK:25 DEF:15"
assert hero_stats :: effective_damage [enemy_stats :: def] == 15
assert enemy_stats :: effective_damage [hero_stats :: def] == 3
assert hero_stats :: effective_damage [100] == 0

# ── 8. Queue implementation ──
class Queue []
    data = {}
    size = 0

    fun enqueue [item]
        this :: data << item
        this :: size += 1
    end

    fun dequeue []
        if this :: size == 0
            raise "Queue is empty"
        end
        # Bug 4 fix: this :: data{0} now works directly
        item = this :: data{0}
        new_data = {}
        loop i in 1..this :: size
            new_data << this :: data{i}
        end
        this :: data = new_data
        this :: size -= 1
        return item
    end

    fun peek []
        if this :: size == 0
            return null
        end
        return this :: data{0}
    end

    fun is_empty []
        return this :: size == 0
    end
end

q = new Queue
assert q :: is_empty []

q :: enqueue ["first"]
q :: enqueue ["second"]
q :: enqueue ["third"]
assert q :: size == 3
assert q :: peek [] == "first"

assert q :: dequeue [] == "first"
assert q :: dequeue [] == "second"
assert q :: size == 1
assert q :: peek [] == "third"

assert q :: dequeue [] == "third"
assert q :: is_empty []

empty_caught = false
begin
    q :: dequeue []
rescue e
    empty_caught = true
end
assert empty_caught

# ── 9. Tree structure with manual linking ──
class TreeNode2 [val, left, right]
end

fun tree_depth [node]
    if node == null
        return 0
    end
    # Recursive calls inlined to avoid variable clobbering
    # (known language limitation: MemoryScope.Set walks parent scopes)
    if tree_depth [node :: left] > tree_depth [node :: right]
        return tree_depth [node :: left] + 1
    end
    return tree_depth [node :: right] + 1
end

leaf = new TreeNode2 [4, null, null]
left_child = new TreeNode2 [2, leaf, null]
right_child = new TreeNode2 [3, null, null]
root = new TreeNode2 [1, left_child, right_child]

assert tree_depth [root] == 3
assert root :: val == 1
assert root :: left :: val == 2
assert root :: right :: val == 3

# ── 10. Exception in recursive function ──
class DepthLimitError [depth]
end

fun safe_recurse [n, max_depth]
    if n > max_depth
        raise new DepthLimitError [n]
    end
    if n == 0
        return 0
    end
    return n + safe_recurse [n - 1, max_depth]
end

assert safe_recurse [5, 10] == 15

depth_error_caught = false
depth_error_val = 0
begin
    safe_recurse [15, 10]
rescue e
    depth_error_caught = true
    assert e is DepthLimitError
    depth_error_val = e :: depth
end
assert depth_error_caught
assert depth_error_val == 15
