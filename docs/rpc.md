# Ways to map rpc method in a RtmpController

## Attributes

### RpcMethodAttribute
marks a method that can be invoked by Rpc service.

### CommandObjectAttribute
map a parameter that is an whole rtmp CommandObject, the parameter has this attribute must be a AmfObject

### FromCommandObjectAttribute
map a parameter that is presents in rtmp CommandObject, when you specificed the Key property, the rpc service will extract an object from CommandObject by key you specificed as method argument, when you not specificed a Key property, Rpc service will use parameter's name instead to find a proper object.

### FromOptionalArgumentAttribute
map a parameter that is presetns in rtmp CommandArgument field, the order of object in CommandArgument field is the order your parameter which has this attribute to call the method. the Name filed is the name that in the command message. if you did't specificed a name, Rpc service will use the method's name.

## About return value
if your method returns a value, the rpc service will return it by invoking `_result`.
if your method throws an expection, the rpc service will return the exception message to peer by invoking `_error`.
if your method returns a Task, rpc service will wait to task completes, then returns the result of the task.
if your method returns void, rpc service will send nothing to peer.

if you wish to return multiple data, or the return rules not satisifies your requirements, you can make your method return void or `Task<void>`(just a Task), and call `_result` or `_error` in the method




