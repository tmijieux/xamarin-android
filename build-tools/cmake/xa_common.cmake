if((CMAKE_VERSION_MAJOR EQUAL 3 AND CMAKE_VERSION_MINOR GREATER_EQUAL 7) OR CMAKE_VERSION_MAJOR GREATER 3)
  set(CMAKE_POLICY_DEFAULT_CMP0066 NEW)
endif()

if((CMAKE_VERSION_MAJOR EQUAL 3 AND CMAKE_VERSION_MINOR GREATER_EQUAL 8) OR CMAKE_VERSION_MAJOR GREATER 3)
  set(CMAKE_POLICY_DEFAULT_CMP0067 NEW)
endif()

set(CMAKE_OSX_DEPLOYMENT_TARGET 10.12)
