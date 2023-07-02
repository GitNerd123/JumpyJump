using Sandbox;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyGame
{
    public class PawnController : EntityComponent<Pawn>
    {
        public int StepSize => 35;
        public int GroundAngle => 45;
        public int JumpSpeed => 400; // Adjust the jump speed to control the vaulting height
        public float Gravity => 800f;
        public float AirAcceleration => 1500f; // Adjust air acceleration for better air control
        public float AirFriction => 0.5f; // Adjust air friction for better air control
        public float DashDistance => 200f; // Adjust the dash distance
        public float DashCooldown => 10f; // Adjust the dash cooldown time in seconds

        HashSet<string> ControllerEvents = new(StringComparer.OrdinalIgnoreCase);
        float lastDashTime = 0f;

        bool Grounded => Entity.GroundEntity.IsValid();

        public int Velocity { get; internal set; }

        public string ParticleEffect { get; set; } = "particles/explosion_fireball.vpcf"; // Set your desired particle effect path here

        public void Simulate(IClient cl)
        {
            ControllerEvents.Clear();

            var movement = Entity.InputDirection.Normal;
            var angles = Entity.ViewAngles.WithPitch(0);
            var moveVector = Rotation.From(angles) * movement * 320f;
            var groundEntity = CheckForGround();

            if (groundEntity.IsValid())
            {
                if (!Grounded)
                {
                    Entity.Velocity = Entity.Velocity.WithZ(0);
                    AddEvent("grounded");
                }

                Entity.Velocity = Accelerate(Entity.Velocity, moveVector.Normal, moveVector.Length, 200.0f * (Input.Down("run") ? 2.5f : 1f), 7.5f);
                Entity.Velocity = ApplyFriction(Entity.Velocity, 4.0f);

                // Auto hop (bunny hop)
                if (Grounded && Input.Down("jump") && !HasEvent("jump"))
                {
                    DoJump();
                }
            }
            else
            {
                Entity.Velocity = Accelerate(Entity.Velocity, moveVector.Normal, moveVector.Length, 100, 20f);
                Entity.Velocity += Vector3.Down * Gravity * Time.Delta;
            }

            var mh = new MoveHelper(Entity.Position, Entity.Velocity);
            mh.Trace = mh.Trace.Size(Entity.Hull).Ignore(Entity);

            if (mh.TryMoveWithStep(Time.Delta, StepSize) > 0)
            {
                if (Grounded)
                {
                    mh.Position = StayOnGround(mh.Position);
                }
                Entity.Position = mh.Position;
                Entity.Velocity = mh.Velocity;
            }

            Entity.GroundEntity = groundEntity;

            // Render the speed on the HUD
            //    Engine.Renderer.DrawText("Speed: " + Entity.Velocity.Length.FloorToInt(), new Vector2(Screen.Width - 100, Screen.Height - 40), Color.White, 12);

            // Check for vaulting
            {
                var trace = Entity.TraceBBox(Entity.Position, Entity.Position + Vector3.Up * StepSize);
                if (trace.Hit && Vector3.GetAngle(Vector3.Up, trace.Normal) <= GroundAngle)
                {
                    // Perform vaulting
                    PerformVault(trace.EndPosition);
                }
            }

            // Check for dashing
            if (Input.Down("run") && Time.Now - lastDashTime >= DashCooldown)
            {
                lastDashTime = Time.Now;
                DoDash(angles);
            }
        }
void PerformVault(Vector3 vaultTarget)
{
    var vaultDirection = (vaultTarget - Entity.Position).Normal;

    // Adjust the jump speed to control the vaulting height if grounded or in the air
    int vaultJumpSpeed = Grounded ? 350 : 600;
    Entity.Velocity += Vector3.Up * vaultJumpSpeed;

    // Check if the pawn can see over the object
    var visibilityTrace = Entity.TraceBBox(Entity.Position + Vector3.Up * StepSize, vaultTarget);
    bool canSeeOverObject = !visibilityTrace.Hit;

    if (canSeeOverObject)
    {
        // Move the player towards the vault target position
        Entity.Position = vaultTarget;

        // Add a vault event
        AddEvent("vault");
    }
}


        void DoJump()
        {
            if (Grounded)
            {
                Entity.Velocity = ApplyJump(Entity.Velocity, "jump");
            }
        }

        void DoDash(Angles viewAngles)
        {
            var dashDirection = Rotation.From(viewAngles).Forward;
            Entity.Velocity += dashDirection * DashDistance;
            AddEvent("dash");
        }

        Entity CheckForGround()
        {
            if (Entity.Velocity.z > 100f)
                return null;

            var trace = Entity.TraceBBox(Entity.Position, Entity.Position + Vector3.Down, 2f);

            if (!trace.Hit)
                return null;

            if (trace.Normal.Angle(Vector3.Up) > GroundAngle)
                return null;

            return trace.Entity;
        }

        Vector3 ApplyFriction(Vector3 input, float frictionAmount)
        {
            float StopSpeed = 100.0f;

            var speed = input.Length;
            if (speed < 0.1f) return input;

            // Bleed off some speed, but if we have less than the bleed
            // threshold, bleed the threshold amount.
            float control = (speed < StopSpeed) ? StopSpeed : speed;

            // Add the amount to the drop amount.
            var drop = control * Time.Delta * frictionAmount;

            // scale the velocity
            float newspeed = speed - drop;
            if (newspeed < 0) newspeed = 0;
            if (newspeed == speed) return input;

            newspeed /= speed;
            input *= newspeed;

            return input;
        }

        Vector3 Accelerate(Vector3 input, Vector3 wishdir, float wishspeed, float speedLimit, float acceleration)
        {
            if (speedLimit > 0 && wishspeed > speedLimit)
                wishspeed = speedLimit;

            var currentspeed = input.Dot(wishdir);
            var addspeed = wishspeed - currentspeed;

            if (addspeed <= 0)
                return input;

            var accelspeed = acceleration * Time.Delta * wishspeed;

            if (accelspeed > addspeed)
                accelspeed = addspeed;

            input += wishdir * accelspeed;

            // Check if the player is air strafing
            if (!Grounded && input.Length < wishspeed)
            {
                var airControl = wishspeed - input.Length;
                var airAccel = airControl * AirAcceleration * Time.Delta;

                if (airAccel > addspeed)
                    airAccel = addspeed;

                input += wishdir * airAccel;
            }

            return input;
        }

        Vector3 ApplyJump(Vector3 input, string jumpType)
        {
            AddEvent(jumpType);

            return input + Vector3.Up * JumpSpeed;
        }

        Vector3 StayOnGround(Vector3 position)
        {
            var start = position + Vector3.Up * 2;
            var end = position + Vector3.Down * StepSize;

            // See how far up we can go without getting stuck
            var trace = Entity.TraceBBox(position, start);
            start = trace.EndPosition;

            // Now trace down from a known safe position
            trace = Entity.TraceBBox(start, end);

            if (trace.Fraction <= 0) return position;
            if (trace.Fraction >= 1) return position;
            if (trace.StartedSolid) return position;
            if (Vector3.GetAngle(Vector3.Up, trace.Normal) > GroundAngle) return position;

            return trace.EndPosition;
        }

        public bool HasEvent(string eventName)
        {
            return ControllerEvents.Contains(eventName);
        }

        void AddEvent(string eventName)
        {
            if (HasEvent(eventName))
                return;

            ControllerEvents.Add(eventName);
        }

        void CreateParticleEffect()
        {
            if (string.IsNullOrEmpty(ParticleEffect))
                return;

            var effect = Particles.Create(ParticleEffect, Entity.Position);
            DelayedDestroy(effect, 2.0f); // DelayedDestroy is a custom function to destroy the effect after a specified duration
        }

        async void DelayedDestroy(Particles effect, float duration)
        {
            await Task.Delay((int)(duration * 1000)); // Convert duration to milliseconds
            effect.Destroy();
        }
    }
}
