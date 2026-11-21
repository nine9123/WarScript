# Real-world scenarios: game entity system, inventory
# management, scoring, leaderboard, text adventure state

# ── 1. Game entity with damage/heal ──
class GameEntity [name, max_hp]
    hp = max_hp
    alive = true

    fun take_damage [amount]
        if !this :: alive
            return
        end
        this :: hp -= amount
        if this :: hp <= 0
            this :: hp = 0
            this :: alive = false
        end
    end

    fun heal [amount]
        if !this :: alive
            return
        end
        this :: hp += amount
        if this :: hp > this :: max_hp
            this :: hp = this :: max_hp
        end
    end

    fun status []
        if this :: alive
            return "{this :: name}: {this :: hp}/{this :: max_hp}"
        end
        return "{this :: name}: DEAD"
    end
end

hero = new GameEntity ["Hero", 100]
assert hero :: status [] == "Hero: 100/100"
hero :: take_damage [30]
assert hero :: status [] == "Hero: 70/100"
hero :: heal [10]
assert hero :: status [] == "Hero: 80/100"
hero :: heal [50]
assert hero :: status [] == "Hero: 100/100"
hero :: take_damage [150]
assert hero :: status [] == "Hero: DEAD"
assert !hero :: alive

# ── 2. Inventory with weight limit ──
class Inventory [max_weight]
    items = {}
    weights = {}
    total_weight = 0

    fun add_item [name, weight]
        if this :: total_weight + weight > this :: max_weight
            return false
        end
        this :: items << name
        this :: weights << weight
        this :: total_weight += weight
        return true
    end

    fun item_count []
        n = 0
        loop i in this :: items
            n += 1
        end
        return n
    end

    fun has_item [name]
        loop i in this :: items
            if i == name
                return true
            end
        end
        return false
    end
end

inv = new Inventory [50]
assert inv :: add_item ["sword", 15]
assert inv :: add_item ["shield", 20]
assert inv :: add_item ["potion", 5]
too_heavy = inv :: add_item ["armor", 30]
assert !too_heavy
assert inv :: item_count [] == 3
assert inv :: total_weight == 40
assert inv :: has_item ["sword"]
has_armor = inv :: has_item ["armor"]
assert !has_armor

# ── 3. Scoring system ──
class ScoreTracker []
    scores = {}
    best = 0
    worst = 999999
    total = 0
    count = 0

    fun record [score]
        this :: scores << score
        this :: total += score
        this :: count += 1
        if score > this :: best
            this :: best = score
        end
        if score < this :: worst
            this :: worst = score
        end
    end

    fun average []
        if this :: count == 0
            return 0
        end
        return this :: total / this :: count
    end
end

tracker = new ScoreTracker
tracker :: record [80]
tracker :: record [95]
tracker :: record [70]
tracker :: record [85]
tracker :: record [90]

assert tracker :: best == 95
assert tracker :: worst == 70
assert tracker :: count == 5
assert tracker :: total == 420
assert tracker :: average [] == 84

# ── 4. Simple state machine for text adventure ──
class Room [name, description]
    exits = {}
    exit_names = {}

    fun add_exit [direction, room_name]
        this :: exits << direction
        this :: exit_names << room_name
    end

    fun get_exit [direction]
        i = 0
        loop e in this :: exits
            if e == direction
                return this :: exit_names{i}
            end
            i += 1
        end
        return null
    end
end

hall = new Room ["Hall", "A grand hall"]
kitchen = new Room ["Kitchen", "A warm kitchen"]
garden = new Room ["Garden", "A sunny garden"]

hall :: add_exit ["north", "Kitchen"]
hall :: add_exit ["east", "Garden"]
kitchen :: add_exit ["south", "Hall"]
garden :: add_exit ["west", "Hall"]

assert hall :: get_exit ["north"] == "Kitchen"
assert hall :: get_exit ["east"] == "Garden"
assert hall :: get_exit ["south"] == null
assert kitchen :: get_exit ["south"] == "Hall"
assert garden :: get_exit ["west"] == "Hall"

# ── 5. Leaderboard (top N tracking) ──
class Leaderboard [max_entries]
    names = {}
    scores = {}

    fun submit [name, score]
        # Insert in sorted position (highest first)
        inserted = false
        new_names = {}
        new_scores = {}
        i = 0
        loop entry_name in this :: names
            entry_score = this :: scores{i}
            if score > entry_score and !inserted
                new_names << name
                new_scores << score
                inserted = true
            end
            new_names << entry_name
            new_scores << entry_score
            i += 1
        end
        if !inserted
            new_names << name
            new_scores << score
        end

        this :: names = new_names
        this :: scores = new_scores

        # Trim to max
        count = 0
        loop n in this :: names
            count += 1
        end
        if count > this :: max_entries
            trimmed_names = {}
            trimmed_scores = {}
            loop j in 0..this :: max_entries
                trimmed_names << this :: names{j}
                trimmed_scores << this :: scores{j}
            end
            this :: names = trimmed_names
            this :: scores = trimmed_scores
        end
    end

    fun top_name []
        return this :: names{0}
    end

    fun top_score []
        return this :: scores{0}
    end
end

lb = new Leaderboard [3]
lb :: submit ["Alice", 100]
lb :: submit ["Bob", 200]
lb :: submit ["Charlie", 150]
lb :: submit ["Dave", 300]
lb :: submit ["Eve", 50]

assert lb :: top_name [] == "Dave"
assert lb :: top_score [] == 300
assert lb :: names == {"Dave", "Bob", "Charlie"}
assert lb :: scores == {300, 200, 150}
