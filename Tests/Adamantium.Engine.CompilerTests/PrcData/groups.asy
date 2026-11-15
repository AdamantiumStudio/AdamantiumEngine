// Nested groups: a blue triangle in the outer group, an orange one shifted by 10 along x in the inner group.
import three;
unitsize(1bp);
currentprojection=orthographic(4,2,3);
triple[] v={(0,0,0),(1,0,0),(0,1,0)};
int[][] vi={{0,1,2}};
begingroup3("Outer");
draw(v,vi,rgb(0.2,0.4,0.6));
begingroup3("Inner");
draw(shift(10,0,0)*v,vi,rgb(0.8,0.6,0.2));
endgroup3();
endgroup3();
