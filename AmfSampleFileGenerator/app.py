import miniamf
from miniamf import amf0, util, xml
import pyamf
from pyamf import remoting
from pyamf.flex import messaging
import uuid

msg = messaging.RemotingMessage(operation='retrieveUser', 
                                destination='so.stdc.flexact.common.User',
                                messageId=str(uuid.uuid4()).upper(),
                                body=['user_id', 'asdfasdfasdf', 'user_id'])
req = remoting.Request(target='UserService', body=[msg])
ev = remoting.Envelope(pyamf.AMF3)
ev['/0'] = req

# Encode request 
bin_msg = remoting.encode(ev)
with open("test.amf3", "wb") as file:
    file.write(bin_msg.getvalue())


buffer = miniamf.encode(1, encoding = miniamf.AMF0)

with open("test.amf0", "wb") as file:
    file.write(buffer.getvalue())

print('a')

