using System;
using System.Collections.Generic;
using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Physics;

// Joints: the game's joint components asked of the physics once their bodies are there, and asked anew when they
// change; the physics thread adds a joint again whenever one of its bodies is made anew.
public sealed partial class PhysicsService
{
    // The game's side.
    private readonly Dictionary<Joint, JointLink> _jointLinks = new();
    private readonly List<Joint> _jointsGone = [];
    private readonly List<int> _freeJointKeys = [];
    // The entities whose joints wait for a body that is not one of its own, by id, said once in the log.
    private readonly HashSet<UInt128> _toldOfJointWaiting = [];
    private int _jointKeyCount;

    // The physics thread's own: the request under each joint key and the joint it holds, the requests the world
    // refused, not tried again until asked anew, and which body add each body key holds.
    private readonly List<JointRequest?> _jointRequests = [];
    private readonly List<JointHandle> _jointHandles = [];
    private readonly HashSet<int> _refusedJoints = [];
    private readonly List<int> _addedAs = [];

    // Asks for each joint component whose bodies are there what it describes, when that differs from what was asked;
    // one gone, or waiting for a body, is taken out.
    private void FollowJoints()
    {
        foreach (var joint in Each<Joint>())
        {
            var request = joint.IsEnabled ? Describe(joint) : null;
            if (!_jointLinks.TryGetValue(joint, out var link))
            {
                if (request == null)
                    continue;

                link = new JointLink(TakeJointKey());
                _jointLinks[joint] = link;
            }

            link.SeenInLook = _lookNumber;
            if (request == link.Sent)
                continue;

            link.Sent = request;
            _commands.Writer.TryWrite(request is { } asked
                ? new PhysicsCommand(PhysicsCommandKind.AddJoint, link.Key, default, default, joint: asked)
                : new PhysicsCommand(PhysicsCommandKind.RemoveJoint, link.Key, default, default));
        }

        foreach (var pair in _jointLinks)
        {
            if (pair.Value.SeenInLook != _lookNumber)
                _jointsGone.Add(pair.Key);
        }

        foreach (var joint in _jointsGone)
        {
            var link = _jointLinks[joint];
            if (link.Sent != null)
                _commands.Writer.TryWrite(new PhysicsCommand(PhysicsCommandKind.RemoveJoint, link.Key, default, default));

            _freeJointKeys.Add(link.Key);
            _jointLinks.Remove(joint);
        }

        _jointsGone.Clear();
    }

    // What the joint asks of the physics, its points and axis on its bodies scaled as each body was made; null while one
    // of its bodies is not there, or it holds its body to itself.
    private JointRequest? Describe(Joint joint)
    {
        var owner = joint.Owner;
        if (!_keys.TryGetValue(owner, out var ownerKey))
            return null;

        var connectedKey = -1;
        var connectedGeneration = 0;
        var connected = joint.ConnectedBody;
        if (connected != null)
        {
            if (connected == owner || !_keys.TryGetValue(connected, out connectedKey))
            {
                // A part of another body's compound, or the joint's own entity, has no other body to hold to; one whose
                // body is not made yet waits quietly.
                if ((connected == owner || !connected.ContainsComponent<RigidBody>()) && _toldOfJointWaiting.Add(owner.Uid))
                    Log.Logger.Warning("The joint of {Entity} holds to {Connected}, which has no rigid body of its own; "
                                       + "the physics leaves the joint", owner.Name, connected.Name);

                return null;
            }

            connectedGeneration = _links[connectedKey].AddedAs;
        }

        var ownerLink = _links[ownerKey];
        var ownerScale = ownerLink.Parts.Scale;
        var request = new JointRequest
        {
            Kind = joint.Kind,
            OwnerKey = ownerKey,
            OwnerGeneration = ownerLink.AddedAs,
            ConnectedKey = connectedKey,
            ConnectedGeneration = connectedGeneration,
            Anchor = joint.Anchor * ownerScale,
            Axis = Vector3F.UnitY,
            CollideConnected = joint.CollideConnected,
            SpringDampingRatio = 1
        };

        // Limits only when used: limits left unused in any order do not refuse the joint.
        return joint switch
        {
            HingeJoint hinge => request with
            {
                Axis = Normalized(hinge.Axis * ownerScale),
                HasLimits = hinge.UsesLimits,
                LowerLimit = hinge.UsesLimits ? float.DegreesToRadians(Math.Clamp(hinge.LowerAngle, -180, 180)) : 0,
                UpperLimit = hinge.UsesLimits ? float.DegreesToRadians(Math.Clamp(hinge.UpperAngle, -180, 180)) : 0,
                MotorSpeed = hinge.UsesMotor ? float.DegreesToRadians(hinge.MotorSpeed) : 0,
                MaxMotorForce = hinge.UsesMotor ? hinge.MaxMotorTorque : 0
            },
            SliderJoint slider => request with
            {
                Axis = Normalized(slider.Axis * ownerScale),
                HasLimits = slider.UsesLimits,
                LowerLimit = slider.UsesLimits ? slider.LowerLimit : 0,
                UpperLimit = slider.UsesLimits ? slider.UpperLimit : 0,
                MotorSpeed = slider.UsesMotor ? slider.MotorSpeed : 0,
                MaxMotorForce = slider.UsesMotor ? slider.MaxMotorForce : 0
            },
            DistanceJoint distance => request with
            {
                ConnectedAnchor = connected != null
                    ? distance.ConnectedAnchor.As<float>() * _links[connectedKey].Parts.Scale
                    : ToPhysics(distance.ConnectedAnchor),
                HasLimits = distance.UsesLimits,
                LowerLimit = distance.UsesLimits ? distance.MinDistance : 0,
                UpperLimit = distance.UsesLimits ? distance.MaxDistance : 0,
                SpringHertz = distance.SpringHertz,
                SpringDampingRatio = distance.SpringDampingRatio
            },
            _ => request
        };
    }

    private static Vector3F Normalized(Vector3F axis) =>
        axis.LengthSquared() > 1e-12f ? Vector3F.Normalize(axis) : Vector3F.Zero;

    private int TakeJointKey()
    {
        if (_freeJointKeys.Count == 0)
            return _jointKeyCount++;

        var key = _freeJointKeys[^1];
        _freeJointKeys.RemoveAt(_freeJointKeys.Count - 1);
        return key;
    }

    // The physics thread: which add of the game's the body under the key is, so a joint asked for an earlier body under
    // the same key is not held to a later one.
    private void SetAddedAs(int key, int addedAs)
    {
        while (_addedAs.Count <= key)
        {
            _addedAs.Add(0);
        }

        _addedAs[key] = addedAs;
    }

    // The physics thread: the joint under the key asked for anew, or taken out for none.
    private void PutJoint(int key, JointRequest? request)
    {
        while (_jointRequests.Count <= key)
        {
            _jointRequests.Add(null);
            _jointHandles.Add(default);
        }

        if (_jointHandles[key].IsValid && _world.Contains(_jointHandles[key]))
            _world.Remove(_jointHandles[key]);

        _jointHandles[key] = default;
        _jointRequests[key] = request;
        _refusedJoints.Remove(key);
    }

    // The physics thread, before it steps: each joint asked for whose bodies are there and that the world does not hold
    // - new, or taken out with a body made anew - is added where its first body's anchor stands now.
    private void KeepJoints()
    {
        for (var key = 0; key < _jointRequests.Count; key++)
        {
            if (_jointRequests[key] is not { } request || _refusedJoints.Contains(key)
                                                       || (_jointHandles[key].IsValid && _world.Contains(_jointHandles[key])))
                continue;

            if (!HoldsBody(request.OwnerKey, request.OwnerGeneration)
                || (request.ConnectedKey >= 0 && !HoldsBody(request.ConnectedKey, request.ConnectedGeneration)))
                continue;

            var owner = _handles[request.OwnerKey];
            var pose = _world.GetPose(owner);
            var connected = request.ConnectedKey >= 0 ? _handles[request.ConnectedKey] : default;
            try
            {
                _jointHandles[key] = _world.Add(new JointDescription
                {
                    Kind = request.Kind,
                    First = owner,
                    Second = connected,
                    Point = pose.TransformPoint(request.Anchor),
                    SecondPoint = connected.IsValid
                        ? _world.GetPose(connected).TransformPoint(request.ConnectedAnchor)
                        : request.ConnectedAnchor,
                    Axis = pose.TransformDirection(request.Axis),
                    CollideConnected = request.CollideConnected,
                    HasLimits = request.HasLimits,
                    LowerLimit = request.LowerLimit,
                    UpperLimit = request.UpperLimit,
                    MotorSpeed = request.MotorSpeed,
                    MaxMotorForce = request.MaxMotorForce,
                    SpringHertz = request.SpringHertz,
                    SpringDampingRatio = request.SpringDampingRatio
                });
            }
            catch (ArgumentException exception)
            {
                Log.Logger.Warning(exception, "The joint of {Entity} cannot be added; the physics leaves it until it changes",
                    _names[request.OwnerKey]);
                _refusedJoints.Add(key);
            }
        }
    }

    // Whether the body under the key is the one the game added as that add: one taken out and another put under the
    // same key since is not.
    private bool HoldsBody(int key, int addedAs) =>
        key < _handles.Count && _handles[key].IsValid && _world.Contains(_handles[key]) && key < _addedAs.Count
        && _addedAs[key] == addedAs;

    private void ForgetJoints()
    {
        _jointLinks.Clear();
        _jointsGone.Clear();
        _freeJointKeys.Clear();
        _toldOfJointWaiting.Clear();
        _jointKeyCount = 0;
        _jointRequests.Clear();
        _jointHandles.Clear();
        _refusedJoints.Clear();
        _addedAs.Clear();
    }
}
