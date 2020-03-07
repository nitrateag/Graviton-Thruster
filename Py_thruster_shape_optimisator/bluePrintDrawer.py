# from visual import *

from Thruster import *

myScene = scene
myScene.height = 800
myScene.width = 1000
myScene.background = vector(0.05,0.05,0.30)
myScene.background = vector(0.05,0.05,0.1)

# myScene.forward = vector(1,0,0)
myScene.forward = vector(0,-1,0)
myScene.up = vector(0,0,1)

# distant_light(direction=vector(0.8,1,0), color=color.gray(0.3))
# myScene.lights[0].visible = False
local_light(pos=myScene.camera.pos, color=color.gray(0.45))

th = thruster(6, 3)
th.ui()

myScene.center = vector(th.diameter/2, th.diameter/2, th.height/2)

th.uiAnim()

