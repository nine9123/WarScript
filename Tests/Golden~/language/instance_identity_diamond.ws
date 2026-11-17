# T3.6 review (5.4, PROP-8): every part of an instance is the same instance
# for ==, including the first part of a diamond base, which the instance's
# relation map no longer holds once the base is allocated again. `this` in
# each constructor body run is collected and compared with the instance.
class A [x]
    global seen
    seen << this
end
class B [x] : A [x]
end
class C [x] : A [x]
end
class D [x] : B [x], C [x]
end
seen = {}
d = new D [1]
loop s in seen
    print s == d
end
print (d as A) == (d as B)
print (d as B) == (d as C)
# expect: true
# expect: true
# expect: true
# expect: true
