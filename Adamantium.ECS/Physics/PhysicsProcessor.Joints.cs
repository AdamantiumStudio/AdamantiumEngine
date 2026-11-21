using System;
using System.Collections.Generic;
using Adamantium.DarkMatter;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.ECS.Physics;

// Joints: the game's joint components asked of the simulation once their bodies are there, and asked anew when they
// change - a body of theirs made anew among the changes.
public partial class PhysicsProcessor
{
    private readonly Dictionary<Joint, JointLink> _jointLinks = new();
    private readonly List<Joint> _jointsGone = [];
    // The entities whose joints wait for a body that is not one of its own, by id, said once in the log.
    private readonly HashSet<UInt128> _toldOfJointWaiting = [];

    // Asks for each joint component whose bodies are there what it describes, when that differs from what was asked;
    // one gone, or waiting for a body, is taken out.
    private void FollowJoints()
    {
        foreach (var joint in _scene.Components.Of<Joint>())
        {
            var request = joint.IsEnabled ? Describe(joint) : null;
            if (!_jointLinks.TryGetValue(joint, out var link))
            {
                if (request == null)
                    continue;

                link = new JointLink();
                _jointLinks[joint] = link;
            }

            link.SeenInLook = _lookNumber;
            if (request == link.Sent)
            {
                TellRefused(joint, link);
                continue;
            }

            TakeOut(link);
            link.Sent = request;
            link.ToldOfRefusal = false;
            if (request is { } asked)
                Add(joint, link, asked);
        }

        foreach (var pair in _jointLinks)
        {
            if (pair.Value.SeenInLook != _lookNumber)
                _jointsGone.Add(pair.Key);
        }

        foreach (var joint in _jointsGone)
        {
            TakeOut(_jointLinks[joint]);
            _jointLinks.Remove(joint);
        }

        _jointsGone.Clear();
    }

    private void Add(Joint joint, JointLink link, in JointRequest request)
    {
        try
        {
            link.Id = _simulation.AddJoint(SettingsOf(request));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Log.Logger.Warning(exception, "The joint of {Entity} cannot be added; the physics leaves it until it changes",
                joint.Owner.Name);
            link.ToldOfRefusal = true;
        }
    }

    // A joint the simulation would not take is said once, and left until it changes.
    private void TellRefused(Joint joint, JointLink link)
    {
        if (link.ToldOfRefusal || link.Id.IsNone || _simulation.StatusOf(link.Id) != PhysicsStatus.Refused)
            return;

        link.ToldOfRefusal = true;
        Log.Logger.Warning("The joint of {Entity} cannot be added; the physics leaves it until it changes: {Reason}",
            joint.Owner.Name, _simulation.RefusalOf(link.Id));
    }

    // Its joint taken out, if the simulation still has it: a body of it taken out took it.
    private void TakeOut(JointLink link)
    {
        if (!link.Id.IsNone && _simulation.StatusOf(link.Id) is not PhysicsStatus.Removed)
            _simulation.RemoveJoint(link.Id);

        link.Id = default;
    }

    private static JointSettings SettingsOf(in JointRequest request) => new()
    {
        Kind = request.Kind,
        First = request.Owner,
        Second = request.Connected,
        Space = JointSpace.Bodies,
        FirstAnchor = request.Anchor,
        SecondAnchor = request.ConnectedAnchor.As<float>(),
        SecondPoint = new WorldPosition(request.ConnectedAnchor),
        Axis = request.Axis,
        CollideConnected = request.CollideConnected,
        Limits = request.HasLimits ? new JointLimits(request.LowerLimit, request.UpperLimit) : null,
        Motor = request.MotorSpeed != 0 || request.MaxMotorForce != 0
            ? new JointMotor(request.MotorSpeed, request.MaxMotorForce)
            : null,
        Spring = request.SpringHertz != 0 ? new JointSpring(request.SpringHertz, request.SpringDampingRatio) : null
    };

    // What the joint asks of the physics, its points and axis carried onto its bodies - each its entity's or the one
    // above it - and scaled as each body was made; null while a body is not there, or it holds a body to itself.
    private JointRequest? Describe(Joint joint)
    {
        var owner = joint.Owner;
        var ownerFound = FindBodyOf(owner, out var ownerKey, out var intoOwner);
        if (ownerFound != BodyFound.Made)
        {
            if (ownerFound == BodyFound.None && _toldOfJointWaiting.Add(owner.Uid))
                Log.Logger.Warning("The joint of {Entity} has no rigid body on its entity or above it; the physics leaves it",
                    owner.Name);

            return null;
        }

        var connected = joint.ConnectedBody;
        var connectedId = default(BodyId);
        var connectedKey = -1;
        var intoConnected = Matrix4x4F.Identity;
        if (connected != null)
        {
            var connectedFound = FindBodyOf(connected, out connectedKey, out intoConnected);
            if (connectedFound != BodyFound.Made || connectedKey == ownerKey)
            {
                // No body to hold to, or the same body: said once; one whose body is not made yet waits quietly.
                if ((connectedFound == BodyFound.None || connectedKey == ownerKey) && _toldOfJointWaiting.Add(owner.Uid))
                    Log.Logger.Warning("The joint of {Entity} holds to {Connected}, which has no other rigid body; the "
                                       + "physics leaves the joint", owner.Name, connected.Name);

                return null;
            }

            connectedId = _links[connectedKey].Id;
        }

        var ownerLink = _links[ownerKey];
        var ownerScale = ownerLink.Parts.Scale;
        var request = new JointRequest
        {
            Kind = joint.Kind,
            Owner = ownerLink.Id,
            Connected = connectedId,
            Anchor = Vector3F.TransformCoordinate(joint.Anchor, intoOwner) * ownerScale,
            Axis = Vector3F.UnitY,
            CollideConnected = joint.CollideConnected,
            SpringDampingRatio = 1
        };

        // Limits only when used: limits left unused in any order do not refuse the joint.
        return joint switch
        {
            HingeJoint hinge => request with
            {
                Axis = Normalized(Vector3F.TransformNormal(hinge.Axis, intoOwner) * ownerScale),
                HasLimits = hinge.UsesLimits,
                LowerLimit = hinge.UsesLimits ? float.DegreesToRadians(Math.Clamp(hinge.LowerAngle, -180, 180)) : 0,
                UpperLimit = hinge.UsesLimits ? float.DegreesToRadians(Math.Clamp(hinge.UpperAngle, -180, 180)) : 0,
                MotorSpeed = hinge.UsesMotor ? float.DegreesToRadians(hinge.MotorSpeed) : 0,
                MaxMotorForce = hinge.UsesMotor ? hinge.MaxMotorTorque : 0
            },
            SliderJoint slider => request with
            {
                Axis = Normalized(Vector3F.TransformNormal(slider.Axis, intoOwner) * ownerScale),
                HasLimits = slider.UsesLimits,
                LowerLimit = slider.UsesLimits ? slider.LowerLimit : 0,
                UpperLimit = slider.UsesLimits ? slider.UpperLimit : 0,
                MotorSpeed = slider.UsesMotor ? slider.MotorSpeed : 0,
                MaxMotorForce = slider.UsesMotor ? slider.MaxMotorForce : 0
            },
            DistanceJoint distance => request with
            {
                ConnectedAnchor = connected != null
                    ? (Vector3F.TransformCoordinate(distance.ConnectedAnchor.As<float>(), intoConnected)
                       * _links[connectedKey].Parts.Scale).As<double>()
                    : distance.ConnectedAnchor,
                HasLimits = distance.UsesLimits,
                LowerLimit = distance.UsesLimits ? distance.MinDistance : 0,
                UpperLimit = distance.UsesLimits ? distance.MaxDistance : 0,
                SpringHertz = distance.SpringHertz,
                SpringDampingRatio = distance.SpringDampingRatio
            },
            _ => request
        };
    }

    // The body of entity - its own, or the one above it as for a collider - and how entity's own space stands in the
    // body's entity's, from the entities' own transforms between them: none of the world's turns, which the physics
    // keeps changing, enter it.
    private BodyFound FindBodyOf(Entity entity, out int key, out Matrix4x4F intoBody)
    {
        intoBody = Matrix4x4F.Identity;
        for (var at = entity; at != null; at = at.Owner)
        {
            if (_keys.TryGetValue(at, out key))
                return BodyFound.Made;

            if (at.ContainsComponent<RigidBody>())
            {
                key = -1;
                return BodyFound.Waiting;
            }

            if (at.Transform is { } transform)
                intoBody *= transform.GetLocalMatrixF();
        }

        key = -1;
        return BodyFound.None;
    }

    private static Vector3F Normalized(Vector3F axis) =>
        axis.LengthSquared() > 1e-12f ? Vector3F.Normalize(axis) : Vector3F.Zero;

    private void ForgetJoints()
    {
        _jointLinks.Clear();
        _jointsGone.Clear();
        _toldOfJointWaiting.Clear();
    }
}
