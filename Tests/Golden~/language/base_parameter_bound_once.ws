# Q-16: passing the same parameter on, a diamond, or bases whose same-named
# parameters both get no argument, give one field per name.
class Named [name]
    fun label []
        return "named {@name}"
    end
end

class Walker [name] : Named [name]
end

class Swimmer [name] : Named [name]
end

class Duck [name] : Walker [name], Swimmer [name]
    fun both []
        return "{@name} walks and swims"
    end
end

d = new Duck ["Donald"]
print d :: both []
print d :: label []
d :: name = "Daisy"
print d :: label []

class Tagged [tag]
end

class Marked [tag]
end

class Plain [x] : Tagged, Marked
    fun show []
        return "{@x} {@tag}"
    end
end

print (new Plain [1]) :: show []
# expect: Donald walks and swims
# expect: named Donald
# expect: named Daisy
# expect: 1 null
