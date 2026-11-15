// A polyline through four points, in black.
import three;
unitsize(1bp);
currentprojection=orthographic(4,2,3);
draw((0,0,0)--(1,0,0)--(1,2,0)--(1,2,3),black);
