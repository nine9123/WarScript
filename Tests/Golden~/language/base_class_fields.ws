# T3.4 (5.7): the fields `@x` is checked against include those of the base
# classes, and an instance has one field list: Bird's `@legs` is the field
# Animal's body assigned, which Animal's methods see too.
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
