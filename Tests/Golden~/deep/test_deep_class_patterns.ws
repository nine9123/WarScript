# Class design patterns: observer, strategy,
# linked list class, computed properties, versioned state

# ── 1. Observer pattern ──
class EventEmitter []
    listeners = {}
    last_event = null

    fun add_listener [name]
        this :: listeners << name
    end

    fun emit [data]
        this :: last_event = data
    end

    fun listener_count []
        count = 0
        loop l in this :: listeners
            count += 1
        end
        return count
    end
end

emitter = new EventEmitter
emitter :: add_listener ["click"]
emitter :: add_listener ["hover"]
emitter :: add_listener ["scroll"]
assert emitter :: listener_count [] == 3
emitter :: emit ["hello"]
assert emitter :: last_event == "hello"

# ── 2. Logger class ──
class AppLogger [prefix]
    entries = {}

    fun write [msg]
        entry = this :: prefix + ": " + msg
        this :: entries << entry
    end

    fun count []
        n = 0
        loop entry in this :: entries
            n += 1
        end
        return n
    end
end

logger = new AppLogger ["APP"]
logger :: write ["started"]
logger :: write ["processing"]
logger :: write ["done"]
assert logger :: count [] == 3

# ── 3. Strategy pattern via composition ──
class BubbleSorter []
    fun sort [arr, n]
        loop i in 0..n - 1
            loop j in 0..n - i - 1
                if arr{j} > arr{j + 1}
                    temp = arr{j}
                    arr{j} = arr{j + 1}
                    arr{j + 1} = temp
                end
            end
        end
        return arr
    end
end

bs = new BubbleSorter
d1 = {5, 3, 8, 1}
bs :: sort [d1, 4]
assert d1 == {1, 3, 5, 8}

# ── 4. Linked list class with methods ──
class LNode [val, nxt]
end

class LList []
    head = null
    length = 0

    fun push [val]
        this :: head = new LNode [val, this :: head]
        this :: length += 1
    end

    fun peek []
        if this :: head == null
            return null
        end
        return this :: head :: val
    end

    fun pop []
        if this :: head == null
            return null
        end
        val = this :: head :: val
        this :: head = this :: head :: nxt
        this :: length -= 1
        return val
    end

    fun to_array []
        result = {}
        current = this :: head
        loop current != null
            result << current :: val
            current = current :: nxt
        end
        return result
    end
end

ll = new LList
ll :: push [3]
ll :: push [2]
ll :: push [1]
assert ll :: length == 3
assert ll :: peek [] == 1
assert ll :: to_array [] == {1, 2, 3}

assert ll :: pop [] == 1
assert ll :: pop [] == 2
assert ll :: length == 1
assert ll :: peek [] == 3

# ── 5. Class with computed properties ──
class Rect [w, h]
    area = w * h
end

r1 = new Rect [5, 3]
assert r1 :: area == 15
r2 = new Rect [4, 4]
assert r2 :: area == 16

# ── 6. Class storing history of changes ──
class Versioned [initial]
    current = initial
    history = {}

    fun update [val]
        this :: history << this :: current
        this :: current = val
    end

    fun version_count []
        count = 0
        loop h in this :: history
            count += 1
        end
        return count + 1
    end
end

v = new Versioned ["draft"]
v :: update ["review"]
v :: update ["final"]
assert v :: current == "final"
assert v :: version_count [] == 3
