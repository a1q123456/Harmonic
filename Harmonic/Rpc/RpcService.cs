using Harmonic.Controllers;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Harmonic.Rpc;

internal class RpcParameter
{
    public bool _isOptional;
    public bool _isFromCommandObject;
    public bool _isCommandObject;
    public bool _isFromOptionalArgument;
    public int _optionalArgumentIndex;
    public string? _commandObjectKey;
    public Type _parameterType;
}

internal class RpcMethod
{
    public string _methodName;
    public MethodInfo _method;

    public List<RpcParameter> _parameters = new();
}

internal class RpcService
{
    public Dictionary<Type, List<RpcMethod>> _controllers = new();

    public void PrepareMethod<T>(T instance, CommandMessage command, out MethodInfo methodInfo, out object?[] callArguments) where T: RtmpController
    {
        if (!_controllers.TryGetValue(instance.GetType(), out var methods))
        {
            throw new EntryPointNotFoundException();
        }
            
        foreach (var method in methods)
        {
            if (method._methodName != command.ProcedureName)
            {
                continue;
            }
            object?[] arguments = new object[method._parameters.Count];
            var i = 0;
            foreach (var para in method._parameters)
            {
                if (para._isCommandObject)
                {
                    arguments[i] = command.CommandObject;
                    i++;
                }
                else if (para._isFromCommandObject)
                {
                    var commandObj = command.CommandObject;
                    object? val = null;
                    if (!commandObj.Fields.TryGetValue(para._commandObjectKey, out val) && !commandObj.DynamicFields.TryGetValue(para._commandObjectKey, out val))
                    {
                        if (para._isOptional)
                        {
                            arguments[i] = Type.Missing;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (para._parameterType.IsInstanceOfType(val))
                    {
                        arguments[i] = val;
                        i++;
                    }
                    else
                    {
                        if (para._isOptional)
                        {
                            arguments[i] = Type.Missing;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else if (para._isFromOptionalArgument)
                {
                    var optionArguments = command.GetType().GetProperties().Where(p => p.GetCustomAttribute<OptionalArgumentAttribute>() != null).ToList();
                    if (para._optionalArgumentIndex >= optionArguments.Count)
                    {
                        if (para._isOptional)
                        {
                            arguments[i] = Type.Missing;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        arguments[i] = optionArguments[i].GetValue(command);
                        i++;
                    }
                }
            }

            if (i == arguments.Length)
            {
                methodInfo = method._method;
                callArguments = arguments;
                return;
            }
        }

        throw new EntryPointNotFoundException();
    }

    internal void CleanupRegistration()
    {
        foreach (var controller in _controllers)
        {
            var gps = controller.Value.GroupBy(m => m._methodName).Where(gp => gp.Count() > 1);

            var hiddenMethods = new List<RpcMethod>();

            foreach (var gp in gps)
            {
                hiddenMethods.AddRange(gp.Where(m => m._method.DeclaringType != controller.Key));
            }
            foreach (var m in hiddenMethods)
            {
                controller.Value.Remove(m);
            }
        }
    }

    internal void RegeisterController(Type controllerType)
    {
        var methods = controllerType.GetMethods();
        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<RpcMethodAttribute>();
            if (attr != null)
            {
                var rpcMethod = new RpcMethod();
                var methodName = attr.Name ?? method.Name;
                var parameters = method.GetParameters();
                bool canInvoke = false;
                var optArgIndex = 0;
                foreach (var para in parameters)
                {
                    var fromCommandObject = para.GetCustomAttribute<FromCommandObjectAttribute>();
                    var fromOptionalArg = para.GetCustomAttribute<FromOptionalArgumentAttribute>();
                    var commandObject = para.GetCustomAttribute<CommandObjectAttribute>();
                    if (fromCommandObject == null && fromOptionalArg == null && commandObject == null)
                    {
                        break;
                    }
                    canInvoke = true;
                    if (fromCommandObject != null)
                    {
                        var name = fromCommandObject.Key ?? para.Name;
                        rpcMethod._parameters.Add(new RpcParameter()
                        {
                            _commandObjectKey = name,
                            _isFromCommandObject = true,
                            _parameterType = para.ParameterType,
                            _isOptional = para.IsOptional
                        });
                    }
                    else if (fromOptionalArg != null)
                    {
                        rpcMethod._parameters.Add(new RpcParameter()
                        {
                            _optionalArgumentIndex = optArgIndex,
                            _isFromOptionalArgument = true,
                            _parameterType = para.ParameterType,
                            _isOptional = para.IsOptional
                        });
                        optArgIndex++;
                    }
                    else if (commandObject != null && para.ParameterType.IsAssignableFrom(typeof(AmfObject)))
                    {
                        rpcMethod._parameters.Add(new RpcParameter()
                        {
                            _isCommandObject = true,
                            _isOptional = para.IsOptional
                        });
                    }
                }
                if (canInvoke || !parameters.Any())
                {
                    rpcMethod._method = method;
                    rpcMethod._methodName = methodName;
                    if (!_controllers.TryGetValue(controllerType, out var mapping))
                    {
                        _controllers.Add(controllerType, new List<RpcMethod>());
                    }
                    _controllers[controllerType].Add(rpcMethod);
                }
            }
        }
    }
}