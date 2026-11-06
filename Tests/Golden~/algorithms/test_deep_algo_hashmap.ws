# test_deep_algo_hashmap.ws — Simple array-based hash map

class HashMap []
    buckets = {}
    bucket_count = 16

    fun init []
        loop i in 0..this :: bucket_count
            this :: buckets << null
        end
    end

    fun hash [key, len]
        h = 0
        loop i in 0..len
            c = key{i}
            if c == "a"
                h += 1
            elif c == "b"
                h += 2
            elif c == "c"
                h += 3
            elif c == "d"
                h += 4
            elif c == "e"
                h += 5
            else
                h += 10
            end
        end
        return h % this :: bucket_count
    end

    fun put [key, key_len, value]
        idx = this :: hash [key, key_len]
        this :: buckets{idx} = value
    end

    fun get [key, key_len]
        idx = this :: hash [key, key_len]
        return this :: buckets{idx}
    end
end

hm = new HashMap
hm :: init []
hm :: put ["abc", 3, "hello"]
hm :: put ["de", 2, "world"]
assert hm :: get ["abc", 3] == "hello"
assert hm :: get ["de", 2] == "world"
