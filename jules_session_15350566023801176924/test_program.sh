#!/bin/bash
cat -n src/SmartGlassesMiddleware/Program.cs | grep "UseRouting"
cat -n src/SmartGlassesMiddleware/Program.cs | grep "UseRateLimiter"
