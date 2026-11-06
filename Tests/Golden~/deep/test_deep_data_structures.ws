# Data structures: ring buffer, priority queue (simple),
# set (array-based), stack with min tracking

# ── 1. Ring buffer ──
class RingBuffer [capacity]
    data = {}
    write_pos = 0
    read_pos = 0
    count = 0

    fun init []
        loop i in 0..this :: capacity
            this :: data << null
        end
    end

    fun write [val]
        this :: data{this :: write_pos} = val
        this :: write_pos = (this :: write_pos + 1) % this :: capacity
        if this :: count < this :: capacity
            this :: count += 1
        else
            this :: read_pos = (this :: read_pos + 1) % this :: capacity
        end
    end

    fun read []
        if this :: count == 0
            return null
        end
        val = this :: data{this :: read_pos}
        this :: read_pos = (this :: read_pos + 1) % this :: capacity
        this :: count -= 1
        return val
    end
end

rb = new RingBuffer [3]
rb :: init []
rb :: write [10]
rb :: write [20]
rb :: write [30]
assert rb :: count == 3
assert rb :: read [] == 10
assert rb :: read [] == 20
assert rb :: count == 1

# Overflow: writing when full pushes out oldest
rb2 = new RingBuffer [3]
rb2 :: init []
rb2 :: write [1]
rb2 :: write [2]
rb2 :: write [3]
rb2 :: write [4]
assert rb2 :: count == 3
assert rb2 :: read [] == 2

# ── 2. Set (array-based, no duplicates) ──
class SimpleSet []
    items = {}

    fun add [val]
        loop existing in this :: items
            if existing == val
                return false
            end
        end
        this :: items << val
        return true
    end

    fun contains [val]
        loop existing in this :: items
            if existing == val
                return true
            end
        end
        return false
    end

    fun remove [val]
        new_items = {}
        found = false
        loop existing in this :: items
            if existing == val and !found
                found = true
            else
                new_items << existing
            end
        end
        this :: items = new_items
        return found
    end

    fun size []
        n = 0
        loop i in this :: items
            n += 1
        end
        return n
    end
end

s = new SimpleSet
assert s :: add [10]
assert s :: add [20]
assert s :: add [30]
added = s :: add [20]
assert !added
assert s :: size [] == 3
assert s :: contains [20]
assert s :: remove [20]
not_found = s :: contains [20]
assert !not_found
assert s :: size [] == 2

# ── 3. MinStack: stack that tracks minimum ──
class MinStack []
    values = {}
    mins = {}
    count = 0

    fun push [val]
        this :: values << val
        if this :: count == 0
            this :: mins << val
        else
            current_min = this :: mins{this :: count - 1}
            if val < current_min
                this :: mins << val
            else
                this :: mins << current_min
            end
        end
        this :: count += 1
    end

    fun pop []
        this :: count -= 1
        val = this :: values{this :: count}
        return val
    end

    fun get_min []
        return this :: mins{this :: count - 1}
    end

    fun peek []
        return this :: values{this :: count - 1}
    end
end

ms = new MinStack
ms :: push [5]
ms :: push [3]
ms :: push [7]
ms :: push [1]
ms :: push [4]

assert ms :: get_min [] == 1
assert ms :: peek [] == 4
ms :: pop []
ms :: pop []
assert ms :: get_min [] == 3
assert ms :: peek [] == 7

# ── 4. Key-Value store with linear search ──
class KVStore []
    keys = {}
    vals = {}

    fun put [key, val]
        # Update existing
        i = 0
        loop k in this :: keys
            if k == key
                this :: vals{i} = val
                return
            end
            i += 1
        end
        this :: keys << key
        this :: vals << val
    end

    fun get [key]
        i = 0
        loop k in this :: keys
            if k == key
                return this :: vals{i}
            end
            i += 1
        end
        return null
    end

    fun has [key]
        loop k in this :: keys
            if k == key
                return true
            end
        end
        return false
    end

    fun size []
        n = 0
        loop k in this :: keys
            n += 1
        end
        return n
    end
end

kv = new KVStore
kv :: put ["name", "Alice"]
kv :: put ["age", 30]
kv :: put ["role", "dev"]
assert kv :: get ["name"] == "Alice"
assert kv :: get ["age"] == 30
assert kv :: has ["role"]
has_missing = kv :: has ["missing"]
assert !has_missing
assert kv :: get ["missing"] == null
assert kv :: size [] == 3

# Update existing key
kv :: put ["age", 31]
assert kv :: get ["age"] == 31
assert kv :: size [] == 3
