# known-failing: T3.4-base-fields
# T3.4 (5.7): the fields `@x` is checked against include those of the base
# classes, so this compiles. At runtime each class part of an instance still
# has its own fields (v1 layout): Bird's `@legs` is a field of the Bird
# part, which Animal's methods do not see, and Bird's methods do not see
# Animal's `legs`. One field list per instance is the rest of T3.4/T3.5.
class Animal [name]
    legs = 4
    fun describe []
        return "{@name} has {@legs} legs"
    end
end

class Bird [name] : Animal [name]
    @legs = 2
    wings = 2
    fun fly []
        return "{@name} flaps {@wings} wings on {@legs} legs"
    end
end

b = new Bird ["Tweety"]
print b :: fly []
print b :: describe []
# expect: Tweety flaps 2 wings on 2 legs
# expect: Tweety has 2 legs
