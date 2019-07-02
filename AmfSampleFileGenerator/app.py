import pyamf
from pyamf import remoting
from pyamf.flex import messaging
import uuid
import random
import os
import shutil
import pathlib
import string
import datetime

msg = messaging.RemotingMessage(operation='retrieveUser', 
                                destination='so.stdc.flexact.common.User',
                                messageId=str(uuid.uuid4()).upper(),
                                body=['user_id', 'asdfasdfasdf', 'user_id'])
req = remoting.Request(target='UserService', body=[msg])
ev = remoting.Envelope(pyamf.AMF3)
ev['/0'] = req

# Encode request 
bin_msg = remoting.encode(ev)


def randomString(stringLength=10):
    """Generate a random string of fixed length """
    letters = string.ascii_lowercase
    return ''.join(random.choice(letters) for i in range(stringLength))

for i in range(0, 10):
    num = random.random() * 10
    buffer = pyamf.encode(num, encoding = pyamf.AMF0)

    pathlib.Path("../samples/amf0/number/").mkdir(parents=True, exist_ok=True)

    with open(f"../samples/amf0/number/{num}.amf0", "wb") as file:
        file.write(buffer.getvalue())

for i in range(0, 10):
    num = randomString()
    buffer = pyamf.encode(num, encoding = pyamf.AMF0)

    pathlib.Path("../samples/amf0/string/").mkdir(parents=True, exist_ok=True)

    with open(f"../samples/amf0/string/{num}.amf0", "wb") as file:
        file.write(buffer.getvalue())

pathlib.Path("../samples/amf0/boolean/").mkdir(parents=True, exist_ok=True)

buffer = pyamf.encode(True, encoding = pyamf.AMF0)
with open(f"../samples/amf0/boolean/true.amf0", "wb") as file:
    file.write(buffer.getvalue())
    
buffer = pyamf.encode(False, encoding = pyamf.AMF0)
with open(f"../samples/amf0/boolean/false.amf0", "wb") as file:
    file.write(buffer.getvalue())
    
pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode(None, encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/null.amf0", "wb") as file:
    file.write(buffer.getvalue())

pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode(pyamf.Undefined, encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/undefined.amf0", "wb") as file:
    file.write(buffer.getvalue())

pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode([1, 2, 3, 4, 'a', 'asdf', 'eee'], encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/array.amf0", "wb") as file:
    file.write(buffer.getvalue())


pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode(datetime.datetime(2019, 2, 11), encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/date.amf0", "wb") as file:
    file.write(buffer.getvalue())

pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode('abc' * 32767, encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/longstring.amf0", "wb") as file:
    file.write(buffer.getvalue())

pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode(pyamf.MixedArray(a=1, b='a', c='a'), encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/ecmaarray.amf0", "wb") as file:
    file.write(buffer.getvalue())

pathlib.Path("../samples/amf0/misc/").mkdir(parents=True, exist_ok=True)
buffer = pyamf.encode(pyamf.xml.fromstring('<a><b value="1" /></a>'), encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/xml.amf0", "wb") as file:
    file.write(buffer.getvalue())

buffer = pyamf.encode({ "a": "b", "c": 1 }, encoding = pyamf.AMF0)
with open(f"../samples/amf0/misc/object.amf0", "wb") as file:
    file.write(buffer.getvalue())

