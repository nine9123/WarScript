# Multi-inheritance edge cases: diamond-like, method from
# each parent, cast chains, property isolation, instanceof
# across complex hierarchies

# ── 1. Three-parent inheritance ──
class HasId [id]
    fun get_id []
        return id
    end
end
class HasName [name]
    fun get_name []
        return name
    end
end
class HasEmail [email]
    fun get_email []
        return email
    end
end
class User [id, name, email] : HasId [id], HasName [name], HasEmail [email]
    fun summary []
        return this :: get_id [] + ":" + this :: get_name [] + ":" + this :: get_email []
    end
end

u = new User [42, "Alice", "alice@test"]
assert u :: summary [] == "42:Alice:alice@test"
assert u :: get_id [] == 42
assert u :: get_name [] == "Alice"
assert u :: get_email [] == "alice@test"
assert u is User
assert u is HasId
assert u is HasName
assert u is HasEmail

# ── 2. Cast to each parent, read and write ──
assert u as HasId :: id == 42
assert u as HasName :: name == "Alice"
assert u as HasEmail :: email == "alice@test"

u as HasName :: name = "Bob"
assert u :: name == "Bob"
assert u as HasName :: name == "Bob"
# Other parents unaffected
assert u as HasId :: id == 42
assert u as HasEmail :: email == "alice@test"

# ── 3. Multiple instances fully independent ──
u1 = new User [1, "One", "one@test"]
u2 = new User [2, "Two", "two@test"]
u1 as HasName :: name = "Changed"
assert u1 :: name == "Changed"
assert u2 :: name == "Two"

# ── 4. Deep chain + multi-parent ──
class Base [tag]
    fun get_tag []
        return tag
    end
end
class Mixin [extra]
    fun get_extra []
        return extra
    end
end
class Mid [tag, extra, level] : Base [tag], Mixin [extra]
end
class Leaf [tag, extra, level, detail] : Mid [tag, extra, level]
    fun info []
        return this :: get_tag [] + "/" + this :: get_extra [] + "/" + level + "/" + detail
    end
end

leaf = new Leaf ["T", "E", 3, "D"]
assert leaf :: info [] == "T/E/3/D"
assert leaf is Leaf
assert leaf is Mid
assert leaf is Base
assert leaf is Mixin
assert leaf as Base :: tag == "T"
assert leaf as Mixin :: extra == "E"

# ── 5. Instanceof negative checks ──
class Unrelated []
end
is_unrelated = leaf is Unrelated
assert !is_unrelated

# ── 6. Multiple classes inheriting same base ──
class Drawable [color]
    fun get_color []
        return color
    end
end
class CircleShape [color, radius] : Drawable [color]
end
class SquareShape [color, side] : Drawable [color]
end

circ = new CircleShape ["red", 5]
sq = new SquareShape ["blue", 10]
assert circ :: get_color [] == "red"
assert sq :: get_color [] == "blue"
assert circ is Drawable
assert sq is Drawable
circ_is_sq = circ is SquareShape
assert !circ_is_sq
sq_is_circ = sq is CircleShape
assert !sq_is_circ

# ── 7. Cast then method call ──
assert circ as Drawable :: get_color [] == "red"
assert sq as Drawable :: get_color [] == "blue"

# ── 8. Inheritance with constructor body computations ──
class Sized [w, h]
    area = w * h
end
class Bordered [w, h, border] : Sized [w, h]
    inner_w = w - 2 * border
    inner_h = h - 2 * border
end

b = new Bordered [100, 60, 5]
assert b :: inner_w == 90
assert b :: inner_h == 50
assert b as Sized :: area == 6000
