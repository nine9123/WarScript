# T3.8: 'is' and 'as' compile for any class of the module set, however many
# constructor arguments it takes, a class defined below the test and a class
# nested in another one; 'is' answers false and 'as' null for an unrelated
# value.
fun check [v]
    return v is Tower
end
class Unit [hp]
    class Part
    end
end
class Tower [range, height]
end
u = new Unit [1]
print check [u]               # expect: false
print u is Unit               # expect: true
print (u as Tower) == null    # expect: true
print 5 is Part               # expect: false
print check [new Tower [1, 2]]   # expect: true
